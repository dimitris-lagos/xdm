using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TraceLog;

namespace XDM.Core.Util
{
    /// <summary>
    /// Activates the desktop window opened by a user-initiated shell command.
    /// ShellExecute and explorer.exe can reuse an existing process, so the
    /// Process returned by Process.Start is not sufficient to find the window.
    /// </summary>
    internal sealed class WindowsForegroundActivator
    {
        private const int SwRestore = 9;
        private readonly string processName;
        private readonly string titleHint;
        private readonly HashSet<IntPtr> windowsBeforeLaunch;

        private WindowsForegroundActivator(string processName, string titleHint)
        {
            this.processName = processName;
            this.titleHint = titleHint;
            windowsBeforeLaunch = FindCandidateWindows(processName).Select(window => window.Handle).ToHashSet();
        }

        internal static WindowsForegroundActivator CaptureForFile(string path)
        {
            var executable = FindAssociatedExecutable(path);
            var processName = String.IsNullOrWhiteSpace(executable)
                ? String.Empty
                : Path.GetFileNameWithoutExtension(executable);
            return new WindowsForegroundActivator(processName, Path.GetFileNameWithoutExtension(path));
        }

        internal static WindowsForegroundActivator CaptureForFolder(string path)
        {
            var normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var title = Path.GetFileName(normalized);
            return new WindowsForegroundActivator("explorer", title);
        }

        internal void ActivateWhenReady(Process? startedProcess)
        {
            _ = Task.Run(async () =>
            {
                IntPtr lastCandidate = IntPtr.Zero;
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    if (attempt > 0) await Task.Delay(75).ConfigureAwait(false);

                    var target = FindBestWindow(startedProcess);
                    if (target == IntPtr.Zero) continue;
                    lastCandidate = target;

                    if (TryActivate(target))
                    {
                        Log.Debug($"Activated shell window 0x{target.ToInt64():X}");
                        return;
                    }
                }

                if (lastCandidate != IntPtr.Zero)
                {
                    TryActivate(lastCandidate);
                    Log.Debug($"Requested activation for shell window 0x{lastCandidate.ToInt64():X}");
                }
                else
                {
                    Log.Debug($"No desktop window found after shell launch ({processName}, {titleHint})");
                }
            });
        }

        private IntPtr FindBestWindow(Process? startedProcess)
        {
            var startedHandle = GetMainWindowHandle(startedProcess);
            if (startedHandle != IntPtr.Zero) return startedHandle;

            var candidates = FindCandidateWindows(processName).ToList();
            var newWindow = candidates.FirstOrDefault(window => !windowsBeforeLaunch.Contains(window.Handle));
            if (newWindow.Handle != IntPtr.Zero) return newWindow.Handle;

            if (!String.IsNullOrWhiteSpace(titleHint))
            {
                var titleMatch = candidates.FirstOrDefault(window =>
                    window.Title.IndexOf(titleHint, StringComparison.OrdinalIgnoreCase) >= 0);
                if (titleMatch.Handle != IntPtr.Zero) return titleMatch.Handle;
            }

            return candidates.Count == 1 ? candidates[0].Handle : IntPtr.Zero;
        }

        private static IntPtr GetMainWindowHandle(Process? process)
        {
            if (process == null) return IntPtr.Zero;
            try
            {
                process.Refresh();
                return process.HasExited ? IntPtr.Zero : process.MainWindowHandle;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private static IEnumerable<WindowInfo> FindCandidateWindows(string processName)
        {
            if (String.IsNullOrWhiteSpace(processName)) return Array.Empty<WindowInfo>();

            var processIds = new HashSet<uint>();
            try
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    using (process) processIds.Add((uint)process.Id);
                }
            }
            catch
            {
                return Array.Empty<WindowInfo>();
            }

            var windows = new List<WindowInfo>();
            EnumWindows((handle, _) =>
            {
                if (!IsWindowVisible(handle)) return true;
                GetWindowThreadProcessId(handle, out var processId);
                if (!processIds.Contains(processId)) return true;

                var titleLength = GetWindowTextLength(handle);
                var title = new StringBuilder(Math.Max(titleLength + 1, 1));
                GetWindowText(handle, title, title.Capacity);
                windows.Add(new WindowInfo(handle, title.ToString()));
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        private static bool TryActivate(IntPtr target)
        {
            if (target == IntPtr.Zero || !IsWindow(target)) return false;
            if (IsIconic(target)) ShowWindowAsync(target, SwRestore);

            var currentThread = GetCurrentThreadId();
            var foreground = GetForegroundWindow();
            var foregroundThread = foreground == IntPtr.Zero
                ? 0u
                : GetWindowThreadProcessId(foreground, out _);
            var targetThread = GetWindowThreadProcessId(target, out _);
            var attachedForeground = foregroundThread != 0 && foregroundThread != currentThread
                && AttachThreadInput(currentThread, foregroundThread, true);
            var attachedTarget = targetThread != 0 && targetThread != currentThread && targetThread != foregroundThread
                && AttachThreadInput(currentThread, targetThread, true);

            try
            {
                BringWindowToTop(target);
                SetForegroundWindow(target);
                return GetForegroundWindow() == target;
            }
            finally
            {
                if (attachedTarget) AttachThreadInput(currentThread, targetThread, false);
                if (attachedForeground) AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        private static string FindAssociatedExecutable(string path)
        {
            var executable = new StringBuilder(1024);
            var result = FindExecutable(path, Path.GetDirectoryName(path), executable);
            return result.ToInt64() > 32 ? executable.ToString() : String.Empty;
        }

        private readonly struct WindowInfo
        {
            internal WindowInfo(IntPtr handle, string title)
            {
                Handle = handle;
                Title = title;
            }

            internal IntPtr Handle { get; }
            internal string Title { get; }
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindExecutable(string lpFile, string? lpDirectory, StringBuilder lpResult);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    }
}
