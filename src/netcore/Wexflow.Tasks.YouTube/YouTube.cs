﻿using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using System;
using System.IO;
using System.Threading;
using System.Xml.Linq;
using System.Xml.XPath;
using Wexflow.Core;

namespace Wexflow.Tasks.YouTube
{
    internal enum PrivacyStatus
    {
        Unlisted,
        Private,
        Public
    }

    public class YouTube : Task
    {
        public string User { get; }
        public string ApplicationName { get; }
        public string ClientSecrets { get; }

        public YouTube(XElement xe, Workflow wf)
            : base(xe, wf)
        {
            User = GetSetting("user");
            ApplicationName = GetSetting("applicationName");
            ClientSecrets = GetSetting("clientSecrets");
        }

        public async override System.Threading.Tasks.Task<TaskStatus> RunAsync()
        {
            Workflow.CancellationTokenSource.Token.ThrowIfCancellationRequested();
            Info("Uploading videos...");

            var succeeded = true;
            var atLeastOneSucceed = false;

            try
            {
                var files = SelectFiles();

                foreach (var file in files)
                {
                    try
                    {
                        Workflow.CancellationTokenSource.Token.ThrowIfCancellationRequested();
                        var xdoc = XDocument.Load(file.Path);

                        foreach (var xvideo in xdoc.XPathSelectElements("/Videos/Video"))
                        {
                            var title = xvideo.Element("Title")!.Value;
                            var desc = xvideo.Element("Description")!.Value;
                            var tags = xvideo.Element("Tags")!.Value.Split(',');
                            var categoryId = xvideo.Element("CategoryId")!.Value;
                            var ps = Enum.Parse<PrivacyStatus>(xvideo.Element("PrivacyStatus")!.Value, true);
                            var filePath = xvideo.Element("FilePath")!.Value;

                            var succeededTask = await UploadVideoAsync(title, desc, tags, categoryId, ps, filePath);
                            succeeded &= succeededTask;

                            if (succeeded && !atLeastOneSucceed)
                            {
                                atLeastOneSucceed = true;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        ErrorFormat("An error occured while uploading the file {0}: {1}", file.Path, e.Message);
                        succeeded = false;
                    }
                    finally
                    {
                        if (!Workflow.CancellationTokenSource.Token.IsCancellationRequested)
                        {
                            WaitOne();
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                ErrorFormat("An error occured while uploading videos: {0}", e.Message);
                return new TaskStatus(Status.Error);
            }

            var status = Status.Success;

            if (!succeeded && atLeastOneSucceed)
            {
                status = Status.Warning;
            }
            else if (!succeeded)
            {
                status = Status.Error;
            }

            Info("Task finished.");
            return new TaskStatus(status);
        }

        private async System.Threading.Tasks.Task<bool> UploadVideoAsync(string title, string desc, string[] tags, string categoryId, PrivacyStatus ps, string filePath)
        {
            try
            {
                InfoFormat("Uploading the video file {0} to YouTube started...", filePath);
                Info("Authentication started...");
                UserCredential credential;
                await using (FileStream stream = new(ClientSecrets, FileMode.Open, FileAccess.Read))
                {
#pragma warning disable CS0618 // Le type ou le membre est obsolète
                    credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                        GoogleClientSecrets.Load(stream).Secrets,
                        // This OAuth 2.0 access scope allows an application to upload files to the
                        // authenticated user's YouTube channel, but doesn't allow other types of access.
                        [YouTubeService.Scope.YoutubeUpload],
                        User,
                        CancellationToken.None
                        );
#pragma warning restore CS0618 // Le type ou le membre est obsolète
                }
                Info("Authentication succeeded.");

                YouTubeService youtubeService = new(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = ApplicationName,
                });

                Video video = new()
                {
                    Snippet = new VideoSnippet
                    {
                        Title = title,
                        Description = desc,
                        Tags = tags,
                        CategoryId = categoryId // See https://developers.google.com/youtube/v3/docs/videoCategories/list
                    },
                    Status = new VideoStatus
                    {
                        PrivacyStatus = ps.ToString().ToLower() // "unlisted" or "private" or "public"
                    }
                };

                await using (FileStream fileStream = new(filePath, FileMode.Open))
                {
                    var videosInsertRequest = youtubeService.Videos.Insert(video, "snippet,status", fileStream, "video/*");
                    videosInsertRequest.ResponseReceived += VideosInsertRequest_ResponseReceived;

                    var res = await videosInsertRequest.UploadAsync();

                    if (res.Exception != null)
                    {
                        ErrorFormat("An error occured while uploading the file {0}: {1}", filePath, res.Exception.Message);
                        return false;
                    }
                }

                InfoFormat("Uploading the video file {0} to YouTube succeeded.", filePath);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                ErrorFormat("An error occured while uploading the video file {0}: {1}", filePath, e.Message);
                return false;
            }
        }

        private void VideosInsertRequest_ResponseReceived(Video video)
        {
            InfoFormat("The video '{0}' was successfully uploaded. Id: '{1}' ", video.Snippet.Title, video.Id);
        }
    }
}
