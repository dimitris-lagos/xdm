using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace XDM.Setup
{
    public sealed class Asset
    {
        public string url { get; set; }
        public string sha256 { get; set; }
        public string downloadSha256 { get; set; }
        public string binarySha256 { get; set; }
    }
    public sealed class MediaPair { public Asset ffmpeg { get; set; } public Asset ytdlp { get; set; } }
    public sealed class MediaVersion { public string version { get; set; } }
    public sealed class MediaConfig
    {
        public MediaVersion ffmpeg { get; set; }
        public MediaVersion ytdlp { get; set; }
        public Dictionary<string, MediaPair> architectures { get; set; }
    }
    public sealed class ReleaseConfig { public string version { get; set; } public MediaConfig media { get; set; } }

    static class Program
    {
        internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        internal static readonly ReleaseConfig Config = Json.Deserialize<ReleaseConfig>(ReadResource("release-config.json"));
        internal static readonly string Arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";
        internal static readonly string InstallDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) is string p && p.Length > 0
            ? p : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "XDM");

        [STAThread]
        static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            if (args.Any(a => a.Equals("/self-test", StringComparison.OrdinalIgnoreCase)))
            {
                using (var work = new Workspace()) { ExtractMsi(work.Path); }
                return 0;
            }
            var quiet = args.Any(a => a.Equals("/quiet", StringComparison.OrdinalIgnoreCase) || a.Equals("/silent", StringComparison.OrdinalIgnoreCase));
            var log = Path.Combine(Path.GetTempPath(), "xdm-setup.log");
            for (int i = 0; i < args.Length - 1; i++) if (args[i].Equals("/log", StringComparison.OrdinalIgnoreCase)) log = Path.GetFullPath(args[i + 1]);
            if (quiet)
            {
                try
                {
                    using (var work = new Workspace())
                    {
                        var selection = new DownloadSelection(!args.Any(a => a.Equals("/skip-yt-dlp", StringComparison.OrdinalIgnoreCase)), !args.Any(a => a.Equals("/skip-ffmpeg", StringComparison.OrdinalIgnoreCase)));
                        DownloadSelected(work.Path, selection, null, null, CancellationToken.None).GetAwaiter().GetResult();
                        var code = Install(work.Path, log, selection, !args.Any(a => a.Equals("/no-desktop-shortcut", StringComparison.OrdinalIgnoreCase)), null);
                        if (!args.Any(a => a.Equals("/no-launch", StringComparison.OrdinalIgnoreCase))) LaunchInstalledApp();
                        return code;
                    }
                }
                catch (Exception ex) { File.AppendAllText(log + ".bootstrapper.log", ex.ToString()); return 1; }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var form = new SetupForm(log)) { Application.Run(form); return form.ExitCode; }
        }

        internal static string ReadResource(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var r = new StreamReader(s ?? throw new InvalidDataException("Missing installer resource: " + name))) return r.ReadToEnd();
        }
        internal static string Hash(string file)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        internal static void CheckHash(string file, string expected)
        {
            if (string.IsNullOrEmpty(expected) || !string.Equals(Hash(file), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Checksum verification failed: " + Path.GetFileName(file));
        }
        internal static string ExtractMsi(string work)
        {
            var name = "xdm-" + Arch + ".msi";
            var target = Path.Combine(work, name);
            using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var dest = File.Create(target)) (source ?? throw new InvalidDataException("Missing MSI")).CopyTo(dest);
            var hashes = Json.Deserialize<Dictionary<string, string>>(ReadResource("package-hashes.json"));
            CheckHash(target, hashes[Arch]);
            if (!Config.media.architectures.ContainsKey(Arch)) throw new InvalidDataException("Missing architecture");
            return target;
        }
        static async Task Download(Asset asset, string target, string expected, IProgress<int> progress, CancellationToken token)
        {
            if (!Uri.TryCreate(asset.url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" || uri.AbsolutePath.Contains("/latest/"))
                throw new InvalidDataException("The installer requires a pinned GitHub release URL.");
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var length = response.Content.Headers.ContentLength;
                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920]; long total = 0; int count;
                    while ((count = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(buffer, 0, count, token).ConfigureAwait(false);
                        total += count;
                        progress?.Report(length.HasValue && length.Value > 0 ? (int)Math.Min(99, total * 100 / length.Value) : 0);
                    }
                }
            }
            CheckHash(target, expected);
            progress?.Report(100);
        }
        internal static Task DownloadPair(string work, IProgress<int> ytProgress, IProgress<int> ffProgress, CancellationToken token)
            => DownloadSelected(work, new DownloadSelection(true, true), ytProgress, ffProgress, token);

        internal static async Task DownloadSelected(string work, DownloadSelection selection, IProgress<int> ytProgress, IProgress<int> ffProgress, CancellationToken token)
        {
            var pair = Config.media.architectures[Arch];
            var tasks = new List<Task>();
            if (selection.Ytdlp) tasks.Add(Download(pair.ytdlp, Path.Combine(work, "yt-dlp.exe"), pair.ytdlp.sha256, ytProgress, token));
            if (selection.Ffmpeg) tasks.Add(DownloadFfmpeg(work, pair.ffmpeg, ffProgress, token));
            await Task.WhenAll(tasks).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        static async Task DownloadFfmpeg(string work, Asset asset, IProgress<int> progress, CancellationToken token)
        {
            var zip = Path.Combine(work, "ffmpeg.zip");
            await Download(asset, zip, asset.downloadSha256, new ForwardProgress(v => progress?.Report(v * 95 / 100)), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            using (var archive = ZipFile.OpenRead(zip))
            {
                archive.Entries.Single(e => e.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                    .ExtractToFile(Path.Combine(work, "ffmpeg.exe"));
                archive.Entries.Single(e => e.Name.Equals("LICENSE.txt", StringComparison.OrdinalIgnoreCase))
                    .ExtractToFile(Path.Combine(work, "FFmpeg-LICENSE.txt"));
            }
            CheckHash(Path.Combine(work, "ffmpeg.exe"), asset.binarySha256);
            progress?.Report(100);
        }
        internal static void LaunchInstalledApp()
        {
            var exe = Path.Combine(InstallDirectory, "xdm-app.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("Installed XDM executable was not found.", exe);
            // Ask the desktop Explorer shell to launch XDM in the interactive user context.
            // https://learn.microsoft.com/windows/win32/shell/samples-execinexplorer
            dynamic desktopShell = GetDesktopShell();
            desktopShell.ShellExecute(exe, "", InstallDirectory, "open", 1);
        }
        internal static object GetDesktopShell()
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            dynamic windows = shell.Windows();
            object location = 0, root = 0;
            int hwnd;
            dynamic desktop = windows.FindWindowSW(ref location, ref root, 8, out hwnd, 1);
            if (desktop == null) throw new InvalidOperationException("The desktop shell is not available. Start XDM from its shortcut.");
            return desktop.Document.Application;
        }

        internal static void VerifySelectedMedia(string work, DownloadSelection selection)
        {
            var pair = Config.media.architectures[Arch];
            if (selection.Ytdlp) CheckHash(Path.Combine(work, "yt-dlp.exe"), pair.ytdlp.sha256);
            if (selection.Ffmpeg)
            {
                CheckHash(Path.Combine(work, "ffmpeg.exe"), pair.ffmpeg.binarySha256);
                if (!File.Exists(Path.Combine(work, "FFmpeg-LICENSE.txt"))) throw new InvalidDataException("Missing FFmpeg license.");
            }
        }
        internal static int Install(string work, string log, DownloadSelection selection, bool desktopShortcut, IProgress<int> progress)
        {
            VerifySelectedMedia(work, selection);
            var msi = ExtractMsi(work);
            var preserved = Path.Combine(work, "preserved");
            Directory.CreateDirectory(preserved);
            var names = new[] { "yt-dlp.exe", "ffmpeg.exe", "FFmpeg-LICENSE.txt" };
            foreach (var name in names)
            {
                var selected = name == "yt-dlp.exe" ? selection.Ytdlp : selection.Ffmpeg;
                var existing = Path.Combine(InstallDirectory, name);
                if (!selected && File.Exists(existing)) File.Copy(existing, Path.Combine(preserved, name));
            }
            var code = NativeInstaller.Install(msi, log, InstallDirectory, desktopShortcut, progress);
            if (code != 0 && code != 3010) throw new IOException("Installation failed with code " + code + ". Log: " + log);
            progress?.Report(95);
            foreach (var name in names)
            {
                var selected = name == "yt-dlp.exe" ? selection.Ytdlp : selection.Ffmpeg;
                var source = Path.Combine(selected ? work : preserved, name);
                if (File.Exists(source)) File.Copy(source, Path.Combine(InstallDirectory, name), true);
            }
            progress?.Report(100);
            return code;
        }
    }

    internal sealed class DownloadSelection
    {
        public bool Ytdlp { get; }
        public bool Ffmpeg { get; }
        public bool All => Ytdlp && Ffmpeg;
        public DownloadSelection(bool ytdlp, bool ffmpeg) { Ytdlp = ytdlp; Ffmpeg = ffmpeg; }
    }
    internal sealed class ForwardProgress : IProgress<int>
    {
        readonly Action<int> report;
        public ForwardProgress(Action<int> report) { this.report = report; }
        public void Report(int value) { report(value); }
    }

    sealed class Workspace : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xdm-setup-" + Guid.NewGuid().ToString("N"));
        public Workspace() { Directory.CreateDirectory(Path); }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }

}
