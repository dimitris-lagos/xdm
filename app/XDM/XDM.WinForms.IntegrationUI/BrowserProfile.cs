using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace XDM.WinForms.IntegrationUI
{
    internal sealed class BrowserProfile
    {
        private BrowserProfile(string name, string extensionPage, params string[] executableCandidates)
        {
            Name = name;
            ExtensionPage = extensionPage;
            ExecutableCandidates = executableCandidates;
        }

        public string Name { get; }
        public string ExtensionPage { get; }
        public IReadOnlyList<string> ExecutableCandidates { get; }

        public string? FindExecutable()
        {
            return ExecutableCandidates.FirstOrDefault(File.Exists);
        }

        public static BrowserProfile FromCommandLine(string? browserName)
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var profiles = new Dictionary<string, BrowserProfile>(StringComparer.OrdinalIgnoreCase)
            {
                ["Chrome"] = new BrowserProfile("Google Chrome", "chrome://extensions/",
                    Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
                    Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe")),
                ["MSEdge"] = new BrowserProfile("Microsoft Edge", "edge://extensions/",
                    Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
                    Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe")),
                ["Opera"] = new BrowserProfile("Opera", "opera://extensions/",
                    Path.Combine(localAppData, "Programs", "Opera", "opera.exe"),
                    Path.Combine(programFiles, "Opera", "opera.exe"),
                    Path.Combine(programFilesX86, "Opera", "opera.exe")),
                ["Brave"] = new BrowserProfile("Brave", "brave://extensions/",
                    Path.Combine(programFiles, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                    Path.Combine(programFilesX86, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
                    Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "Application", "brave.exe")),
                ["Vivaldi"] = new BrowserProfile("Vivaldi", "vivaldi://extensions/",
                    Path.Combine(localAppData, "Vivaldi", "Application", "vivaldi.exe"),
                    Path.Combine(programFiles, "Vivaldi", "Application", "vivaldi.exe"),
                    Path.Combine(programFilesX86, "Vivaldi", "Application", "vivaldi.exe"))
            };

            return browserName != null && profiles.TryGetValue(browserName, out var profile)
                ? profile
                : profiles["Chrome"];
        }
    }
}
