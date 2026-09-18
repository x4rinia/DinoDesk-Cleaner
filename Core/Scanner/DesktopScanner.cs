using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DinoDeskCleaner.Models;

namespace DinoDeskCleaner.Core.Scanner
{
    public class DesktopScanner
    {
        private static readonly Dictionary<string, ItemCategory> ExtensionMap = new()
        {
            // Bilder
            { ".png", ItemCategory.Bild }, { ".jpg", ItemCategory.Bild }, { ".jpeg", ItemCategory.Bild },
            { ".gif", ItemCategory.Bild }, { ".webp", ItemCategory.Bild }, { ".bmp", ItemCategory.Bild }, { ".svg", ItemCategory.Bild },
            
            // Audio
            { ".mp3", ItemCategory.Audio }, { ".wav", ItemCategory.Audio }, { ".flac", ItemCategory.Audio },
            { ".ogg", ItemCategory.Audio }, { ".m4a", ItemCategory.Audio },
            
            // Videos
            { ".mp4", ItemCategory.Video }, { ".mkv", ItemCategory.Video }, { ".avi", ItemCategory.Video },
            { ".mov", ItemCategory.Video }, { ".webm", ItemCategory.Video },
            
            // Archive
            { ".zip", ItemCategory.Archiv }, { ".rar", ItemCategory.Archiv }, { ".7z", ItemCategory.Archiv },
            { ".tar", ItemCategory.Archiv }, { ".gz", ItemCategory.Archiv },
            
            // Dokumente
            { ".doc", ItemCategory.Dokument }, { ".docx", ItemCategory.Dokument }, { ".pdf", ItemCategory.Dokument },
            { ".txt", ItemCategory.Dokument }, { ".xls", ItemCategory.Dokument }, { ".xlsx", ItemCategory.Dokument },
            { ".ppt", ItemCategory.Dokument }, { ".pptx", ItemCategory.Dokument }, { ".odt", ItemCategory.Dokument },
            
            // Installer / Apps
            { ".msi", ItemCategory.Installer }, { ".msix", ItemCategory.Installer }, { ".appx", ItemCategory.Installer },
            { ".appxbundle", ItemCategory.Installer }, { ".exe", ItemCategory.Installer },
            
            // Verknüpfungen
            { ".lnk", ItemCategory.Verknuepfung }, { ".url", ItemCategory.Verknuepfung }
        };

        public string GetDesktopPath()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        public List<DesktopItem> ScanDesktop(string desktopPath)
        {
            var items = new List<DesktopItem>();

            if (!Directory.Exists(desktopPath)) return items;

            // Dateien scannen
            foreach (var filePath in Directory.GetFiles(desktopPath))
            {
                var item = new DesktopItem(filePath);
                item.Category = DetermineCategory(item);
                
                // Verknüpfungen beim normalen Scan ignorieren
                if (item.Category != ItemCategory.Verknuepfung)
                {
                    items.Add(item);
                }
            }

            // Ordner scannen
            foreach (var dirPath in Directory.GetDirectories(desktopPath))
            {
                // Versteckte / Systemordner ignorieren
                var dirInfo = new DirectoryInfo(dirPath);
                if (dirInfo.Attributes.HasFlag(FileAttributes.Hidden) || dirInfo.Attributes.HasFlag(FileAttributes.System))
                    continue;

                // Ignoriere den "Aufräumen" Ordner
                if (dirInfo.Name.Equals("Aufräumen", StringComparison.OrdinalIgnoreCase))
                    continue;

                var item = new DesktopItem(dirPath) { Category = ItemCategory.Ordner };
                items.Add(item);
            }

            return items;
        }

        public List<DesktopItem> ScanShortcuts(string desktopPath)
        {
            var items = new List<DesktopItem>();

            if (!Directory.Exists(desktopPath)) return items;

            foreach (var filePath in Directory.GetFiles(desktopPath))
            {
                var item = new DesktopItem(filePath);
                item.Category = DetermineCategory(item);
                
                if (item.Category == ItemCategory.Verknuepfung)
                {
                    items.Add(item);
                }
            }
            return items;
        }

        private ItemCategory DetermineCategory(DesktopItem item)
        {
            if (ExtensionMap.TryGetValue(item.Extension, out var category))
            {
                // Extra-Prüfung für EXE-Dateien (könnte Portable-App oder sonstiges sein)
                if (category == ItemCategory.Installer && item.Extension == ".exe")
                {
                    // Eine einfache heuristische Prüfung auf Installer-Namen
                    string nameLower = item.Name.ToLowerInvariant();
                    if (nameLower.Contains("setup") || nameLower.Contains("install") || nameLower.Contains("update"))
                    {
                        return ItemCategory.Installer;
                    }
                    else
                    {
                        // TODO: Später FileVersionInfo Hersteller auslesen für bessere Erkennung
                        return ItemCategory.Unknown; // Als "Unbekannt" anzeigen für manuelle Entscheidung
                    }
                }
                
                return category;
            }

            return ItemCategory.Unknown;
        }
    }
}
