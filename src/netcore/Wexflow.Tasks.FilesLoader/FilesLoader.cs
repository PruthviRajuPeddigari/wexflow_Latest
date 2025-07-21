﻿using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using Wexflow.Core;

namespace Wexflow.Tasks.FilesLoader
{
    public class FilesLoader : Task
    {
        public string[] Folders { get; }
        public string[] FlFiles { get; }
        public string RegexPattern { get; }
        public bool Recursive { get; }

        public FilesLoader(XElement xe, Workflow wf) : base(xe, wf)
        {
            Folders = GetSettings("folder");
            FlFiles = GetSettings("file");
            RegexPattern = GetSetting("regexPattern", "");
            Recursive = bool.Parse(GetSetting("recursive", "false"));
        }

        public override TaskStatus Run()
        {
            Workflow.CancellationTokenSource.Token.ThrowIfCancellationRequested();
            Info("Loading files...");

            var success = true;

            try
            {
                if (Recursive)
                {
                    foreach (var folder in Folders)
                    {
                        var files = GetFilesRecursive(folder);

                        foreach (var file in files)
                        {
                            Workflow.CancellationTokenSource.Token.ThrowIfCancellationRequested();
                            if (string.IsNullOrEmpty(RegexPattern) || Regex.IsMatch(file, RegexPattern))
                            {
                              FileInf fi = new(file, Id);
                              Files.Add(fi);
                              InfoFormat("File loaded: {0}", file);
                            }
                            if (!Workflow.CancellationTokenSource.Token.IsCancellationRequested)
                            {
                                WaitOne();
                            }
                        }
                    }
                }
                else
                {
                    foreach (var folder in Folders)
                    {
                        foreach (var file in Directory.GetFiles(folder))
                        {
                            Workflow.CancellationTokenSource.Token.ThrowIfCancellationRequested();
                            if (string.IsNullOrEmpty(RegexPattern) || Regex.IsMatch(file, RegexPattern))
                            {
                              FileInf fi = new(file, Id);
                              Files.Add(fi);
                              InfoFormat("File loaded: {0}", file);
                            }
                            if (!Workflow.CancellationTokenSource.Token.IsCancellationRequested)
                            {
                                WaitOne();
                            }
                        }
                    }
                }

                foreach (var file in FlFiles)
                {
                    if (File.Exists(file))
                    {
                        Files.Add(new FileInf(file, Id));
                        InfoFormat("File loaded: {0}", file);
                    }
                    else
                    {
                        ErrorFormat("File not found: {0}", file);
                        success = false;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                ErrorFormat("An error occured while loading files.", e);
                success = false;
            }

            var status = Status.Success;

            if (!success)
            {
                status = Status.Error;
            }

            Info("Task finished.");
            return new TaskStatus(status, false);
        }

        private static string[] GetFilesRecursive(string dir)
        {
            return Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
        }
    }
}
