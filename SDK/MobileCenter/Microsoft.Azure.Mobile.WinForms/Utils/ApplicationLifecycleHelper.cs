﻿using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Microsoft.Azure.Mobile.Utils
{
    public class ApplicationLifecycleHelper : IApplicationLifecycleHelper
    {
        #region WinEventHook

        private delegate void WinEventDelegate(IntPtr winEventHookHandle, uint eventType, IntPtr windowHandle, int objectId, int childId, uint eventThreadId, uint eventTimeInMilliseconds);
        
        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr eventHookAssemblyHandle, WinEventDelegate eventHookHandle, uint processId, uint threadId, uint dwFlags);
        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        private const uint WINEVENT_OUTOFCONTEXT = 0;

        // Need to ensure delegate is not collected while we're using it,
        // storing it in a class field is simplest way to do this.
        private static WinEventDelegate hookDelegate = new WinEventDelegate(WinEventHook);
        private static bool suspended = false;
        private static Action Minimize;
        private static Action Restore;

        private static void WinEventHook(IntPtr winEventHookHandle, uint eventType, IntPtr windowHandle, int objectId, int childId, uint eventThreadId, uint eventTimeInMilliseconds)
        {
            // Filter out non-HWND
            if (objectId != 0 || childId != 0)
            {
                return;
            }
            var anyNotMinimized = Application.OpenForms.Cast<Form>().Any(form => form.WindowState != FormWindowState.Minimized);
            if (suspended && anyNotMinimized)
            {
                suspended = false;
                Restore?.Invoke();
            }
            else if (!suspended && !anyNotMinimized)
            {
                suspended = true;
                Minimize?.Invoke();
            }
        }

        static ApplicationLifecycleHelper()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            // We need Windows thread ID, not managed
            var threadId = AppDomain.GetCurrentThreadId();
#pragma warning restore CS0618 // Type or member is obsolete
            var hook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, hookDelegate, 0, (uint)threadId, WINEVENT_OUTOFCONTEXT);
            Application.ApplicationExit += delegate { UnhookWinEvent(hook); };
        }

        #endregion

        public ApplicationLifecycleHelper()
        {
            Enabled = true;
            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
            {
                UnhandledExceptionOccurred?.Invoke(sender, new UnhandledExceptionOccurredEventArgs((Exception)eventArgs.ExceptionObject));
            };
        }

        private void InvokeResuming()
        {
            ApplicationResuming?.Invoke(null, EventArgs.Empty);
        }

        private void InvokeSuspended()
        {
            ApplicationSuspended?.Invoke(null, EventArgs.Empty);
        }

        private bool enabled;
        public bool Enabled
        {
            get
            {
                return enabled;
            }
            set
            {
                if (value == enabled)
                {
                    return;
                }
                if (value)
                {
                    Restore += InvokeResuming;
                    Minimize += InvokeSuspended;
                }
                else
                {
                    Restore -= InvokeResuming;
                    Minimize -= InvokeSuspended;
                }
                enabled = value;
            }
        }

        public event EventHandler ApplicationSuspended;
        public event EventHandler ApplicationResuming;
        public event EventHandler<UnhandledExceptionOccurredEventArgs> UnhandledExceptionOccurred;
    }
}
