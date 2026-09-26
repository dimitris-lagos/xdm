using System;
using System.IO;

namespace XDM.WinForms.IntegrationUI
{
    internal sealed class ExtensionPackage
    {
        public ExtensionPackage(string name, string description, string folderName)
        {
            Name = name;
            Description = description;
            FolderName = folderName;
        }

        public string Name { get; }
        public string Description { get; }
        public string FolderName { get; }
        public string FolderPath => Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FolderName));
        public bool IsAvailable => File.Exists(Path.Combine(FolderPath, "manifest.json"));
    }
}
