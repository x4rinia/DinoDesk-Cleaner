using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DinoDeskCleaner.Models
{
    public enum ItemCategory
    {
        Unknown,
        Bild,
        Audio,
        Video,
        Archiv,
        Dokument,
        Installer,
        Verknuepfung,
        Ordner
    }

    public partial class DesktopItem : ObservableObject
    {
        [ObservableProperty]
        private string name = string.Empty;

        [ObservableProperty]
        private string fullPath = string.Empty;

        [ObservableProperty]
        private string extension = string.Empty;

        [ObservableProperty]
        private ItemCategory category;

        [ObservableProperty]
        private long sizeInBytes;

        [ObservableProperty]
        private bool isSelected = true;

        [ObservableProperty]
        private bool isSelectedForAction = false;

        // Shortcut properties
        [ObservableProperty]
        private string shortcutTarget = string.Empty;

        [ObservableProperty]
        private bool isShortcutValid = true;

        [ObservableProperty]
        private string shortcutStatus = "✔ OK";

        [ObservableProperty]
        private int runCount = 0;

        [ObservableProperty]
        private double usageScore = 0;

        [ObservableProperty]
        private DateTime createdAt = DateTime.Now;

        public DesktopItem()
        {
        }

        public DesktopItem(string path)
        {
            FullPath = path;
            Name = Path.GetFileName(path);
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                Extension = fi.Extension.ToLowerInvariant();
                SizeInBytes = fi.Length;
                CreatedAt = fi.CreationTime;
            }
            else if (Directory.Exists(path))
            {
                Extension = "";
                Category = ItemCategory.Ordner;
                try { CreatedAt = Directory.GetCreationTime(path); } catch { }
            }
        }
    }
}
