using System;
using System.Collections.Generic;
using System.Linq;
using DinoDeskCleaner.Models;

namespace DinoDeskCleaner.Core.Action
{
    public class FolderSuggestion
    {
        public string SuggestedName { get; set; } = string.Empty;
        public List<DesktopItem> Items { get; set; } = new();
    }

    public class FolderSuggester
    {
        public List<FolderSuggestion> SuggestFolders(List<DesktopItem> items)
        {
            var suggestions = new List<FolderSuggestion>();
            var assignedItemPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var categoryDefinitions = new List<(string CategoryName, string[] Keywords)>
            {
                ("🎮 Spiele & Launcher", new[] {
                    "steam", "epic", "gog", "battle.net", "riot", "origin", "ubisoft", "ea ", "ea launcher",
                    "minecraft", "roblox", "league", "valorant", "wow", "warcraft", "ascension", "simulator",
                    "game", "witcher", "cyberpunk", "fallout", "diablo", "starcraft", "counter-strike", "csgo", "dota"
                }),

                ("💻 Entwicklung & Coding", new[] {
                    "code", "visual studio", "studio", "git", "node", "python", "java", "intellij", "pycharm",
                    "eclipse", "php", "xampp", "docker", "postman", "unity", "unreal", "godot", "antigravity",
                    "terminal", "powershell", "rust", "golang", "sql", "database", "dbeaver", "heidisql"
                }),

                ("🎨 Grafik & Design", new[] {
                    "photoshop", "illustrator", "premiere", "blender", "figma", "gimp", "canva", "paint",
                    "inkscape", "cinema 4d", "clip studio", "lightroom"
                }),

                ("🎵 Audio & Musik", new[] {
                    "spotify", "audacity", "fl studio", "ableton", "cubase", "itunes", "foobar", "reaper",
                    "melodc", "melodic", "audio", "sound", "virtualdj", "traktor"
                }),

                ("🎬 Video & Streaming", new[] {
                    "obs", "streamlabs", "davinci", "handbrake", "twitch", "vlc", "potplayer", "kodi", "capcut"
                }),

                ("🌐 Browser & Internet", new[] {
                    "chrome", "firefox", "edge", "opera", "brave", "safari", "vivaldi", "tor browser"
                }),

                ("💬 Kommunikation & Chat", new[] {
                    "discord", "teams", "skype", "zoom", "slack", "thunderbird", "outlook", "telegram", "whatsapp", "signal"
                }),

                ("⬇️ Downloads & Tools", new[] {
                    "jdownloader", "7-zip", "7z", "winrar", "bittorrent", "utorrent", "qbittorrent", "filezilla",
                    "rufus", "anydesk", "teamviewer"
                }),

                ("📄 Office & Dokumente", new[] {
                    "word", "excel", "powerpoint", "acrobat", "reader", "pdf", "notion", "obsidian", "evernote",
                    "libreoffice", "openoffice", "calc", "writer"
                }),

                ("🛡️ Sicherheit & Tuning", new[] {
                    "ccleaner", "antivirus", "malwarebytes", "avira", "kaspersky", "bitdefender", "cpu-z", "gpu-z",
                    "hwmonitor", "afterburner", "geforce", "adrenalin", "process explorer"
                })
            };

            foreach (var (categoryName, keywords) in categoryDefinitions)
            {
                var matched = items
                    .Where(i => i.Category == ItemCategory.Verknuepfung && !assignedItemPaths.Contains(i.FullPath))
                    .Where(i =>
                    {
                        string nameLower = i.Name.ToLowerInvariant();
                        string targetLower = (i.ShortcutTarget ?? "").ToLowerInvariant();
                        return keywords.Any(k => nameLower.Contains(k) || targetLower.Contains(k));
                    })
                    .ToList();

                if (matched.Count > 0)
                {
                    suggestions.Add(new FolderSuggestion
                    {
                        SuggestedName = categoryName,
                        Items = matched
                    });

                    foreach (var m in matched)
                    {
                        assignedItemPaths.Add(m.FullPath);
                    }
                }
            }

            // Normale Bild-Dateien auf dem Desktop separat gruppieren (falls vorhanden)
            var unassignedImages = items
                .Where(i => i.Category == ItemCategory.Bild && !assignedItemPaths.Contains(i.FullPath))
                .ToList();

            if (unassignedImages.Count > 0)
            {
                suggestions.Add(new FolderSuggestion
                {
                    SuggestedName = "🖼️ Bilder & Grafiken",
                    Items = unassignedImages
                });
                foreach (var img in unassignedImages) assignedItemPaths.Add(img.FullPath);
            }

            // Normale Dokument-Dateien auf dem Desktop gruppieren (falls vorhanden)
            var unassignedDocs = items
                .Where(i => i.Category == ItemCategory.Dokument && !assignedItemPaths.Contains(i.FullPath))
                .ToList();

            if (unassignedDocs.Count > 0)
            {
                suggestions.Add(new FolderSuggestion
                {
                    SuggestedName = "📁 Dokumente",
                    Items = unassignedDocs
                });
            }

            return suggestions;
        }
    }
}
