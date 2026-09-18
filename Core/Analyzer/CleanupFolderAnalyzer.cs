using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using DinoDeskCleaner.Core.Action;

namespace DinoDeskCleaner.Core.Analyzer
{
    public partial class CleanupCandidate : ObservableObject
    {
        [ObservableProperty]
        private string name = string.Empty;

        [ObservableProperty]
        private string relativePath = string.Empty;

        [ObservableProperty]
        private string fullPath = string.Empty;

        [ObservableProperty]
        private long sizeInBytes;

        public string FormattedSize => RecycleBinManager.FormatBytes(SizeInBytes);

        [ObservableProperty]
        private string reason = string.Empty;

        [ObservableProperty]
        private bool isSelected = true;
    }

    public class CleanupFolderAnalyzer
    {
        private static readonly Regex DuplicatePattern = new(@"\s*\(\d+\)\.[^.]+$|\s*-\s*Kopie(?:\s*\(\d+\))?\.[^.]+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public List<CleanupCandidate> AnalyzeCleanupFolder(string aufräumenRoot)
        {
            var candidates = new List<CleanupCandidate>();
            if (!Directory.Exists(aufräumenRoot)) return candidates;

            try
            {
                var files = Directory.GetFiles(aufräumenRoot, "*", SearchOption.AllDirectories)
                    .Select(p => new FileInfo(p))
                    .Where(fi => !fi.Attributes.HasFlag(FileAttributes.Hidden) && !fi.Attributes.HasFlag(FileAttributes.System))
                    .ToList();

                // 1. Muster für Kopien/Doubletten wie "Heroic Motif (1).mp3" oder "... - Kopie.png"
                foreach (var fi in files)
                {
                    if (DuplicatePattern.IsMatch(fi.Name))
                    {
                        candidates.Add(new CleanupCandidate
                        {
                            Name = fi.Name,
                            FullPath = fi.FullName,
                            RelativePath = Path.GetRelativePath(aufräumenRoot, fi.FullName),
                            SizeInBytes = fi.Length,
                            Reason = "Doublette / Kopie-Dateiname (z.B. (1) oder Kopie)"
                        });
                    }
                    // 2. Temporäre / Backup-Dateien
                    else if (fi.Name.StartsWith("sess_", StringComparison.OrdinalIgnoreCase) ||
                             fi.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase) ||
                             fi.Extension.Equals(".bak", StringComparison.OrdinalIgnoreCase) ||
                             fi.Extension.Equals(".pdump", StringComparison.OrdinalIgnoreCase))
                    {
                        candidates.Add(new CleanupCandidate
                        {
                            Name = fi.Name,
                            FullPath = fi.FullName,
                            RelativePath = Path.GetRelativePath(aufräumenRoot, fi.FullName),
                            SizeInBytes = fi.Length,
                            Reason = $"Temporäre / Backup-Datei ({fi.Extension})"
                        });
                    }
                }

                var candidatePaths = new HashSet<string>(candidates.Select(c => c.FullPath), StringComparer.OrdinalIgnoreCase);

                // 3. Inhaltliche Doubletten (gleiche Bytegröße und gleicher Hash)
                var sizeGroups = files
                    .Where(f => f.Length > 1024 && !candidatePaths.Contains(f.FullName))
                    .GroupBy(f => f.Length)
                    .Where(g => g.Count() > 1);

                foreach (var group in sizeGroups)
                {
                    var hashDictionary = new Dictionary<string, FileInfo>();
                    foreach (var file in group)
                    {
                        string hash = ComputeFileHash(file.FullName);
                        if (string.IsNullOrEmpty(hash)) continue;

                        if (hashDictionary.TryGetValue(hash, out var original))
                        {
                            candidates.Add(new CleanupCandidate
                            {
                                Name = file.Name,
                                FullPath = file.FullName,
                                RelativePath = Path.GetRelativePath(aufräumenRoot, file.FullName),
                                SizeInBytes = file.Length,
                                Reason = $"Exakte inhaltliche Doublette zu '{original.Name}'"
                            });
                        }
                        else
                        {
                            hashDictionary[hash] = file;
                        }
                    }
                }
            }
            catch
            {
            }

            return candidates;
        }

        private static string ComputeFileHash(string filePath)
        {
            try
            {
                using var md5 = MD5.Create();
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] buffer = new byte[65536]; // Schneller Sample-/Voll-Hash
                int read = stream.Read(buffer, 0, buffer.Length);
                byte[] hash = md5.ComputeHash(buffer, 0, read);
                return Convert.ToHexString(hash);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
