using System;
using System.IO;
using System.Runtime.InteropServices;

namespace XDM.Setup
{
    // Windows Installer records provide estimated work units, not elapsed-time guesses.
    internal sealed class InstallProgress
    {
        long total, position, perAction;
        bool backward, script;
        public int Percent { get; private set; }
        public void Reset(int ticks, bool reverse, bool generatingScript)
        {
            total = Math.Max(0, ticks); backward = reverse; script = generatingScript;
            position = backward ? total : 0; perAction = 0; Update();
        }
        public void ActionInfo(int ticks, bool enabled) { perAction = enabled ? Math.Max(0, ticks) : 0; }
        public void ActionData() { Advance(perAction); }
        public void Advance(long ticks) { position += backward ? -ticks : ticks; Update(); }
        public void AddTotal(int ticks) { total += Math.Max(0, ticks); if (backward) position += Math.Max(0, ticks); Update(); }
        void Update()
        {
            if (total <= 0) return;
            var done = Math.Max(0, Math.Min(total, backward ? total - position : position));
            var estimate = script ? (int)(done * 15 / total) : 15 + (int)(done * 79 / total);
            Percent = Math.Max(Percent, Math.Min(94, estimate));
        }
    }

    internal static class NativeInstaller
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int RecordHandler(IntPtr context, uint message, uint record);
        [DllImport("msi.dll", EntryPoint = "MsiSetExternalUIRecord")]
        static extern uint SetHandler(RecordHandler handler, uint filter, IntPtr context, out IntPtr previous);
        [DllImport("msi.dll", EntryPoint = "MsiSetExternalUIRecord")]
        static extern uint RestoreHandler(IntPtr handler, uint filter, IntPtr context, out IntPtr previous);
        [DllImport("msi.dll")] static extern int MsiRecordGetInteger(uint record, uint field);
        [DllImport("msi.dll")] static extern uint MsiSetInternalUI(uint level, IntPtr window);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern uint MsiEnableLog(uint mode, string file, uint attributes);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)] static extern uint MsiInstallProduct(string package, string properties);

        internal static int Install(string msi, string log, string directory, bool desktopShortcut, IProgress<int> progress)
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(log));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            var logging = MsiEnableLog(0x3fff, log, 0);
            if (logging != 0) throw new IOException("Could not create installation log. Code: " + logging);
            var counter = new InstallProgress();
            RecordHandler handler = (context, message, record) =>
            {
                try
                {
                    switch (message & 0xff000000)
                    {
                        case 0x0a000000:
                            var kind = MsiRecordGetInteger(record, 1);
                            var ticks = MsiRecordGetInteger(record, 2);
                            switch (kind)
                            {
                                case 0: counter.Reset(ticks, MsiRecordGetInteger(record, 3) == 1, MsiRecordGetInteger(record, 4) == 1); break;
                                case 1: counter.ActionInfo(ticks, MsiRecordGetInteger(record, 3) == 1); break;
                                case 2: counter.Advance(ticks); break;
                                case 3: counter.AddTotal(ticks); break;
                            }
                            break;
                        case 0x09000000: counter.ActionData(); break;
                    }
                    progress?.Report(counter.Percent);
                    return 1; // IDOK: progress/action-data message was handled.
                }
                catch { return 0; }
            };
            var previousLevel = MsiSetInternalUI(2, IntPtr.Zero); // INSTALLUILEVEL_NONE
            IntPtr previousHandler = IntPtr.Zero;
            var enabled = false;
            try
            {
                var status = SetHandler(handler, (1u << 10) | (1u << 9), IntPtr.Zero, out previousHandler);
                if (status != 0) throw new IOException("Could not monitor installation. Code: " + status);
                enabled = true;
                return (int)MsiInstallProduct(msi, "REBOOT=ReallySuppress CREATE_DESKTOP_SHORTCUT=" + (desktopShortcut ? "1" : "0") + " INSTALLFOLDER=\"" + directory + "\"");
            }
            finally
            {
                if (enabled) RestoreHandler(previousHandler, 0, IntPtr.Zero, out _);
                MsiSetInternalUI(previousLevel, IntPtr.Zero);
                MsiEnableLog(0, null, 0);
                GC.KeepAlive(handler);
            }
        }
    }
}
