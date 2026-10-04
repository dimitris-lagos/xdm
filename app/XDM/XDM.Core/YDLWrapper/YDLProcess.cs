using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using TraceLog;
using XDM.Core;
using XDM.Core.Util;

namespace YDLWrapper
{
    public class YDLProcess
    {
        public Uri? Uri { get; set; }
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public string? JsonOutputFile { get; set; }
        public string? BrowserName { get; set; } //Fetch cookies from browser
        public bool SingleVideo { get; set; }
        public int TimeoutMilliseconds { get; set; } = 120000;

        private Process? ydlProc;
        private readonly object processGate = new();
        private bool cancelled;

        public void Cancel()
        {
            lock (processGate)
            {
                cancelled = true;
                if (ydlProc != null)
                {
                    try
                    {
                        if (!ydlProc.HasExited && Environment.OSVersion.Platform == PlatformID.Win32NT)
                        {
                            using var killer = Process.Start(new ProcessStartInfo
                            {
                                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskkill.exe"),
                                Arguments = "/PID " + ydlProc.Id + " /T /F",
                                UseShellExecute = false, CreateNoWindow = true,
                                RedirectStandardOutput = true, RedirectStandardError = true
                            });
                            if (killer == null || !killer.WaitForExit(3000) || killer.ExitCode != 0)
                                if (!ydlProc.HasExited) ydlProc.Kill();
                        }
                        else if (!ydlProc.HasExited) ydlProc.Kill();
                    }
                    catch { }
                }
            }
        }
        public void Start()
        {
            var exec = FindYDLBinary();
            var pb = new ProcessStartInfo
            {
                FileName = exec.Path,
            };

            var fetchCookieArgs = string.Empty;
            if (exec.BinaryType == YtBinaryType.YtDlp && !string.IsNullOrEmpty(BrowserName))
            {
                fetchCookieArgs = $"--cookies-from-browser {BrowserName}";
            }

            var sb = new StringBuilder();
            if (SingleVideo) sb.Append(" --no-playlist");
            // A running desktop process can retain PATH from before Deno was installed.
            var deno = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".deno", "bin", "deno.exe");
            if (exec.BinaryType == YtBinaryType.YtDlp && File.Exists(deno))
                sb.Append(" --js-runtimes ").Append(QuoteArgument("deno:" + deno));
            foreach (var arg in new string[] {
                "--no-warnings", "-q", "-i", "-J",
                fetchCookieArgs,
                QuoteArgument(Uri!.ToString()) })
            {
                sb.Append(" " + arg);
            }

            if (!string.IsNullOrEmpty(UserName))
            {
                sb.Append(" --username ").Append(QuoteArgument(UserName));
                if (!string.IsNullOrEmpty(Password))
                {
                    sb.Append(" --password ").Append(QuoteArgument(Password));
                }
            }

            pb.Arguments = sb.ToString();

            Log.Debug("Running video extractor: " + exec.Path);

            pb.RedirectStandardOutput = true;
            pb.CreateNoWindow = true;
            pb.UseShellExecute = false;
            pb.RedirectStandardError = true;
            pb.RedirectStandardInput = false;
            pb.StandardOutputEncoding = Encoding.UTF8;
            JsonOutputFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            Log.Debug("Opening youtube-dl json file: " + JsonOutputFile);
            using var fs = new FileStream(JsonOutputFile,
                FileMode.Create, FileAccess.ReadWrite);

            try
            {
                lock (processGate)
                {
                    if (cancelled) throw new OperationCanceledException();
                    ydlProc = Process.Start(pb);
                }
                ydlProc.OutputDataReceived += (a, b) =>
                {
                    if (b.Data != null)
                    {
                        var bytes = Encoding.UTF8.GetBytes(b.Data);
                        //Console.WriteLine(b.Data);
                        fs.Write(bytes, 0, bytes.Length);
                    }
                };
                ydlProc.ErrorDataReceived += (a, b) =>
                {
                    if (b.Data != null)
                    {
                        Log.Debug(b.Data);
                    }
                };

                ydlProc.BeginOutputReadLine();
                ydlProc.BeginErrorReadLine();
                if (!ydlProc.WaitForExit(TimeoutMilliseconds))
                {
                    ydlProc.Kill();
                    ydlProc.WaitForExit();
                    throw new TimeoutException("Video extraction timed out");
                }
                // Wait again to flush asynchronous output callbacks.

                ydlProc.WaitForExit();
                fs.Close();

                var exitCode = ydlProc.ExitCode;

                if (ydlProc.ExitCode != 0)
                {
                    Log.Debug("Non-zero error code from youtube-dl: " + ydlProc.ExitCode);
                    throw new Exception("Non-zero error code from youtube-dl: " + ydlProc.ExitCode);
                }
            }
            finally
            {
                lock (processGate)
                {
                    ydlProc?.Dispose();
                    ydlProc = null;
                }
            }
        }

        private static string QuoteArgument(string value)
        {
            var result = new StringBuilder("\"");
            var slashes = 0;
            foreach (var ch in value)
            {
                if (ch == '\\') { slashes++; continue; }
                if (ch == '"') result.Append('\\', slashes * 2 + 1);
                else result.Append('\\', slashes);
                result.Append(ch);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }

        private static YtBinaryType GetYtBinaryType(string executableName)
        {
            if (executableName.StartsWith("yt-dlp"))
            {
                return YtBinaryType.YtDlp;
            }
            return YtBinaryType.Yt;
        }

        public static YtBinary FindYDLBinary()
        {
            //var executableName = Environment.OSVersion.Platform == PlatformID.Win32NT ? "youtube-dl.exe" : "youtube-dl";
            var executableNames = Environment.OSVersion.Platform == PlatformID.Win32NT
                ? new string[] { "yt-dlp.exe", "yt-dlp_x86.exe", "youtube-dl.exe" }
                : new string[] { "yt-dlp", "yt-dlp_linux", "youtube-dl" };
            string? binPath = null;
            string? execName = null;
            var found = false;
            foreach (var executableName in executableNames)
            {
                execName = executableName;
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, executableName);
                if (File.Exists(path))
                {
                    found = true;
                    binPath = path;
                    break;
                }
                path = Path.Combine(Config.AppDir, executableName);
                if (File.Exists(path))
                {
                    found = true;
                    binPath = path;
                    break;
                }
                var ydlPathEnvVar = Environment.GetEnvironmentVariable("YOUTUBEDL_HOME");
                if (ydlPathEnvVar != null)
                {
                    path = Path.Combine(ydlPathEnvVar, executableName);
                    if (File.Exists(path))
                    {
                        found = true;
                        binPath = path;
                        break;
                    }
                }
                path = PlatformHelper.FindExecutableFromSystemPath(executableName);
                if (path != null)
                {
                    found = true;
                    binPath = path;
                    break;
                }
            }
            if (found)
            {
                return new YtBinary { BinaryType = GetYtBinaryType(execName!), Path = binPath! };
            }
            throw new FileNotFoundException("YoutubeDL executable not found");
        }


        //private void ProcessJson()
        //{
        //    using (StreamReader reader = File.OpenText(JsonOutputFile))
        //    {
        //        JObject o = (JObject)JToken.ReadFrom(new JsonTextReader(reader));
        //        o[]
        //    }
        //}
    }
}
