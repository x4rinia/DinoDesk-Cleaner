using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using DinoDeskCleaner.Models;

namespace DinoDeskCleaner.Core.Scanner
{
    public class UsageAnalyzer
    {
        public class UsageData
        {
            public string Name { get; set; } = string.Empty;
            public int RunCount { get; set; }
            public string ExecutablePath { get; set; } = string.Empty;
        }

        private List<UsageData>? _usageCache = null;

        public void AnalyzeUsage(DesktopItem item)
        {
            if (item == null) return;
            if (item.Category != ItemCategory.Verknuepfung && item.Category != ItemCategory.Installer)
            {
                return;
            }

            try
            {
                if (_usageCache == null)
                {
                    _usageCache = LoadUserAssistData();
                }

                string searchName = (item.Name ?? "").Replace(".lnk", "").ToLowerInvariant();
                string targetFileName = SafeGetFileNameWithoutExtension(item.ShortcutTarget);

                var match = _usageCache?.FirstOrDefault(u =>
                    (!string.IsNullOrEmpty(searchName) && (u.Name ?? "").ToLowerInvariant().Contains(searchName)) ||
                    (!string.IsNullOrEmpty(searchName) && !string.IsNullOrEmpty(u.ExecutablePath) && u.ExecutablePath.ToLowerInvariant().Contains(searchName)) ||
                    (!string.IsNullOrEmpty(targetFileName) && !string.IsNullOrEmpty(u.ExecutablePath) && u.ExecutablePath.ToLowerInvariant().Contains(targetFileName.ToLowerInvariant())));

                if (match != null)
                {
                    item.RunCount = match.RunCount;
                }

                // Score berechnen (RunCount im Verhältnis zum Dateialter)
                DateTime creation = item.CreatedAt > DateTime.MinValue ? item.CreatedAt : DateTime.Now.AddDays(-30);
                double daysOld = Math.Max(1.0, (DateTime.Now - creation).TotalDays);
                item.UsageScore = item.RunCount / daysOld;
            }
            catch
            {
                // Verwendungsanalyse darf niemals abbrechen
            }
        }

        public List<UsageData> GetTopUsedProgramsNotInList(IEnumerable<string> existingNames, int count = 5)
        {
            try
            {
                if (_usageCache == null)
                {
                    _usageCache = LoadUserAssistData();
                }

                var existingNamesLower = (existingNames ?? Enumerable.Empty<string>())
                    .Select(n => (n ?? "").Replace(".lnk", "").ToLowerInvariant())
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToHashSet();

                var suggestions = _usageCache
                    .Where(u => !string.IsNullOrEmpty(u.ExecutablePath) && u.ExecutablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    .Where(u => !existingNamesLower.Contains((u.Name ?? "").ToLowerInvariant()))
                    .OrderByDescending(u => u.RunCount)
                    .Take(count)
                    .ToList();

                return suggestions;
            }
            catch
            {
                return new List<UsageData>();
            }
        }

        private List<UsageData> LoadUserAssistData()
        {
            var results = new List<UsageData>();
            try
            {
                string[] guids = {
                    "{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}",
                    "{F4E57C4B-2036-45F0-A9AB-443BCFE33D9F}"
                };

                foreach (var guid in guids)
                {
                    try
                    {
                        string path = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{guid}\Count";
                        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(path);
                        if (key != null)
                        {
                            foreach (string valueName in key.GetValueNames())
                            {
                                try
                                {
                                    if (key.GetValue(valueName) is byte[] data && data.Length >= 8)
                                    {
                                        string decodedName = DecodeRot13(valueName);
                                        int runCount = BitConverter.ToInt32(data, 4);

                                        if (runCount > 0)
                                        {
                                            string resolvedPath = ResolveKnownFolder(decodedName);
                                            string displayName = SafeGetFileNameWithoutExtension(resolvedPath);
                                            if (string.IsNullOrWhiteSpace(displayName)) displayName = resolvedPath;
                                            results.Add(new UsageData { Name = displayName, ExecutablePath = resolvedPath, RunCount = runCount });
                                        }
                                    }
                                }
                                catch
                                {
                                    // Fehlerhafter Registry-Eintrag überspringen
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Registry-Zweig überspringen
                    }
                }
            }
            catch
            {
                // Globaler Fallback
            }

            return results;
        }

        private static string SafeGetFileNameWithoutExtension(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try
            {
                return Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string DecodeRot13(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            StringBuilder result = new StringBuilder(input.Length);
            foreach (char c in input)
            {
                if (c >= 'a' && c <= 'z')
                {
                    result.Append((char)('a' + (c - 'a' + 13) % 26));
                }
                else if (c >= 'A' && c <= 'Z')
                {
                    result.Append((char)('A' + (c - 'A' + 13) % 26));
                }
                else
                {
                    result.Append(c);
                }
            }
            return result.ToString();
        }

        private static readonly Dictionary<string, string> KnownFolderMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "{6D809377-6AF0-444B-8957-A3773F02200E}", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) },
            { "{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) },
            { "{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}", Environment.GetFolderPath(Environment.SpecialFolder.System) },
            { "{D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27}", Environment.GetFolderPath(Environment.SpecialFolder.SystemX86) },
            { "{F38BF404-1D43-42F2-9305-67DE0B28FC23}", Environment.GetFolderPath(Environment.SpecialFolder.Windows) },
            { "{5E6C858F-0E22-4760-9AFE-EA3317B67173}", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) },
            { "{A52BBA46-E9E1-435F-B3D9-28DAA648C0F6}", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) },
            { "{82A5EA35-D9CD-47C5-9629-E15D2F714E6E}", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) },
        };

        public static string ResolveKnownFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            foreach (var kvp in KnownFolderMap)
            {
                if (path.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return path.Replace(kvp.Key, kvp.Value);
                }
            }
            return path;
        }
    }
}
