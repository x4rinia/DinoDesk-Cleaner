using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace DinoDeskCleaner.Core.Action
{
    public class RecycleBinInfo
    {
        public long ItemCount { get; set; }
        public long SizeInBytes { get; set; }
        public string FormattedSize => RecycleBinManager.FormatBytes(SizeInBytes);
        public string DisplaySummary => ItemCount == 0
            ? "Papierkorb ist leer (0 Elemente)."
            : $"Papierkorb enthält {ItemCount} Elemente ({FormattedSize}).";
        public List<string> DriveDetails { get; } = new();
    }

    public class RecycleBinEmptyResult
    {
        public bool Success { get; set; }
        public int ErrorCode { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public long RemainingItems { get; set; }
        public string RemainingSizeFormatted { get; set; } = "0,00 MB";
    }

    public class DeletedItem
    {
        public string Name { get; set; } = string.Empty;
        public string OriginalLocation { get; set; } = string.Empty;
        public DateTime DateDeleted { get; set; }
        public long SizeInBytes { get; set; }
        public string FormattedSize => RecycleBinManager.FormatBytes(SizeInBytes);
        public object? ShellFolderItem { get; set; }
    }

    public class RecycleBinManager
    {
        [Flags]
        public enum RecycleFlags : uint
        {
            SHERB_NOCONFIRMATION = 0x00000001,
            SHERB_NOPROGRESSUI = 0x00000002,
            SHERB_NOSOUND = 0x00000004
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, RecycleFlags dwFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        public static string FormatBytes(long bytes)
        {
            var deCulture = CultureInfo.GetCultureInfo("de-DE");
            if (bytes <= 0) return "0,00 MB";
            if (bytes < 1024) return $"{bytes} Bytes";
            if (bytes < 1024L * 1024) return (bytes / 1024.0).ToString("F2", deCulture) + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024.0)).ToString("F2", deCulture) + " MB";
            return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("F2", deCulture) + " GB";
        }

        public static bool SendToRecycleBin(string path)
        {
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path)) return false;
                var shf = new SHFILEOPSTRUCT
                {
                    wFunc = 0x0003, // FO_DELETE
                    pFrom = path + '\0' + '\0', // Double null-terminated
                    fFlags = 0x0040 | 0x0010 | 0x0004 // FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT
                };
                return SHFileOperation(ref shf) == 0;
            }
            catch
            {
                return false;
            }
        }

        public RecycleBinInfo GetRecycleBinInfo()
        {
            var result = new RecycleBinInfo();
            var checkedDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var drives = DriveInfo.GetDrives();
                foreach (var drive in drives)
                {
                    try
                    {
                        if (!drive.IsReady) continue;
                        if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable) continue;

                        checkedDrives.Add(drive.Name);
                        var sqrbi = new SHQUERYRBINFO
                        {
                            cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO))
                        };

                        int hr = SHQueryRecycleBin(drive.Name, ref sqrbi);
                        if (hr == 0)
                        {
                            result.ItemCount += sqrbi.i64NumItems;
                            result.SizeInBytes += sqrbi.i64Size;

                            if (sqrbi.i64NumItems > 0)
                            {
                                result.DriveDetails.Add($"{drive.Name.TrimEnd('\\')}: {sqrbi.i64NumItems} Elemente ({FormatBytes(sqrbi.i64Size)})");
                            }
                        }
                    }
                    catch
                    {
                        // Laufwerk überspringen falls nicht verfügbar
                    }
                }

                if (result.ItemCount == 0)
                {
                    var overallInfo = new SHQUERYRBINFO
                    {
                        cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO))
                    };
                    int hr = SHQueryRecycleBin(null, ref overallInfo);
                    if (hr == 0 && overallInfo.i64NumItems > 0)
                    {
                        result.ItemCount = overallInfo.i64NumItems;
                        result.SizeInBytes = overallInfo.i64Size;
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        public RecycleBinEmptyResult EmptyRecycleBin(IProgress<(double percent, string text)>? progress = null)
        {
            var result = new RecycleBinEmptyResult();
            const RecycleFlags flags = RecycleFlags.SHERB_NOCONFIRMATION | RecycleFlags.SHERB_NOPROGRESSUI | RecycleFlags.SHERB_NOSOUND;
            int lastError = 0;

            try
            {
                progress?.Report((5, "Papierkörbe aller Laufwerke werden vorbereitet..."));

                var allDrives = DriveInfo.GetDrives()
                    .Where(d =>
                    {
                        try { return d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable); }
                        catch { return false; }
                    })
                    .ToList();

                int total = allDrives.Count;
                if (total == 0) total = 1;

                for (int i = 0; i < allDrives.Count; i++)
                {
                    var drive = allDrives[i];
                    double startPct = 10 + (i * 75.0 / total);
                    progress?.Report((startPct, $"Laufwerk {drive.Name.TrimEnd('\\')} wird geleert... ({i + 1} von {total})"));

                    try
                    {
                        int hr = SHEmptyRecycleBin(IntPtr.Zero, drive.Name, flags);
                        if (hr != 0 && hr != unchecked((int)0x80070002) && hr != unchecked((int)0x80004005))
                        {
                            lastError = hr;
                        }
                    }
                    catch
                    {
                    }

                    double endPct = 10 + ((i + 1) * 75.0 / total);
                    progress?.Report((endPct, $"Laufwerk {drive.Name.TrimEnd('\\')} bereinigt."));
                }

                progress?.Report((90, "Abschließende Bereinigung wird ausgeführt..."));
                int hrGlobal = SHEmptyRecycleBin(IntPtr.Zero, null, flags);
                if (hrGlobal != 0 && hrGlobal != unchecked((int)0x80070002) && hrGlobal != unchecked((int)0x80004005))
                {
                    lastError = hrGlobal;
                }

                progress?.Report((95, "Papierkorb-Status wird aktualisiert..."));

                var postInfo = GetRecycleBinInfo();
                result.RemainingItems = postInfo.ItemCount;
                result.RemainingSizeFormatted = postInfo.FormattedSize;

                if (postInfo.ItemCount == 0)
                {
                    result.Success = true;
                    progress?.Report((100, "Papierkorb erfolgreich geleert!"));
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = lastError;
                    result.ErrorMessage = lastError != 0
                        ? $"HRESULT: 0x{lastError:X8} ({Marshal.GetExceptionForHR(lastError)?.Message ?? "Fehler"})"
                        : "Einige Dateien sind möglicherweise blockiert.";
                    progress?.Report((100, "Vorgang beendet (einige Elemente verblieben)."));
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                progress?.Report((100, $"Fehler: {ex.Message}"));
            }

            return result;
        }

        public DeletedItem? GetLastDeletedItem()
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return null;
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic recycleBin = shell.NameSpace(10); // ssfBITBUCKET
                dynamic items = recycleBin.Items();

                DeletedItem? newestItem = null;

                for (int i = 0; i < items.Count; i++)
                {
                    dynamic item = items.Item(i);
                    try
                    {
                        string name = recycleBin.GetDetailsOf(item, 0);
                        string originalPath = recycleBin.GetDetailsOf(item, 1);
                        string dateStr = recycleBin.GetDetailsOf(item, 2);
                        
                        DateTime dateDeleted = DateTime.MinValue;
                        if (!string.IsNullOrEmpty(dateStr))
                        {
                            if (DateTime.TryParse(dateStr, out DateTime parsed))
                            {
                                dateDeleted = parsed;
                            }
                        }

                        if (newestItem == null || dateDeleted > newestItem.DateDeleted)
                        {
                            newestItem = new DeletedItem
                            {
                                Name = name,
                                OriginalLocation = originalPath,
                                DateDeleted = dateDeleted,
                                ShellFolderItem = item
                            };
                        }
                    }
                    catch { }
                }

                return newestItem;
            }
            catch
            {
                return null;
            }
        }

        public bool RestoreItem(DeletedItem item, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (item.ShellFolderItem == null)
            {
                errorMessage = "Element existiert nicht mehr.";
                return false;
            }

            try
            {
                dynamic folderItem = item.ShellFolderItem;
                dynamic verbs = folderItem.Verbs();
                bool restored = false;

                for (int i = 0; i < verbs.Count; i++)
                {
                    dynamic verb = verbs.Item(i);
                    string verbName = verb.Name?.ToString() ?? "";
                    verbName = verbName.Replace("&", "");

                    if (verbName.Equals("Wiederherstellen", StringComparison.OrdinalIgnoreCase) ||
                        verbName.Equals("Restore", StringComparison.OrdinalIgnoreCase) ||
                        verbName.Equals("undelete", StringComparison.OrdinalIgnoreCase))
                    {
                        verb.DoIt();
                        restored = true;
                        break;
                    }
                }

                if (!restored)
                {
                    // Fallback
                    folderItem.InvokeVerb("undelete");
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }
    }
}
