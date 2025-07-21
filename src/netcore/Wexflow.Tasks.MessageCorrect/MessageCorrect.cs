﻿using System;
using System.Threading;
using System.Xml.Linq;
using Wexflow.Core;

namespace Wexflow.Tasks.MessageCorrect
{
    public class MessageCorrect : Task
    {
        public string CheckString { get; }

        public MessageCorrect(XElement xe, Workflow wf) : base(xe, wf)
        {
            CheckString = GetSetting("checkString");
        }

        public override TaskStatus Run()
        {
            try
            {
                Workflow.CancellationTokenSource.Token.ThrowIfCancellationRequested();
                var o = SharedMemory["message"];
                var message = o == null ? string.Empty : o.ToString();
                var result = message!.Contains(CheckString, StringComparison.CurrentCulture);
                Info($"The result is {result}");

                return new TaskStatus(result ? Status.Success : Status.Error, result, message);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ErrorFormat("An error occured.", ex);
                return new TaskStatus(Status.Error);
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
}
