using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using DinoDeskCleaner.Models;

namespace DinoDeskCleaner.Core.Scanner
{
    public class ShortcutAnalyzer
    {
        public void AnalyzeShortcut(DesktopItem item)
        {
            if (item == null) return;
            if (item.Category != ItemCategory.Verknuepfung) return;

            try
            {
                if (string.Equals(item.Extension, ".url", StringComparison.OrdinalIgnoreCase))
                {
                    AnalyzeUrlShortcut(item);
                    return;
                }

                if (string.Equals(item.Extension, ".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    AnalyzeLnkShortcut(item);
                    return;
                }

                // Unbekannte Verknüpfungserweiterung
                item.IsShortcutValid = false;
                item.ShortcutStatus = "⚠️ Nicht prüfbar";
                item.ShortcutTarget = "Unbekanntes Verknüpfungsformat";
            }
            catch (Exception ex)
            {
                item.IsShortcutValid = false;
                item.ShortcutStatus = "⚠️ Nicht prüfbar";
                if (string.IsNullOrWhiteSpace(item.ShortcutTarget))
                {
                    item.ShortcutTarget = $"Fehler: {ex.Message}";
                }
            }
        }

        private void AnalyzeUrlShortcut(DesktopItem item)
        {
            try
            {
                if (!File.Exists(item.FullPath))
                {
                    item.IsShortcutValid = false;
                    item.ShortcutStatus = "❌ Defekt";
                    item.ShortcutTarget = "Datei existiert nicht";
                    return;
                }

                var lines = File.ReadAllLines(item.FullPath);
                string? url = null;
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    {
                        url = trimmed.Substring(4).Trim();
                        break;
                    }
                }

                if (!string.IsNullOrWhiteSpace(url))
                {
                    item.ShortcutTarget = url;
                    item.IsShortcutValid = true;
                    item.ShortcutStatus = "✔ OK (Web)";
                }
                else
                {
                    item.ShortcutTarget = "Keine gültige URL in Datei";
                    item.IsShortcutValid = false;
                    item.ShortcutStatus = "❌ Defekt";
                }
            }
            catch (Exception ex)
            {
                item.ShortcutTarget = $"Nicht lesbar ({ex.Message})";
                item.IsShortcutValid = false;
                item.ShortcutStatus = "⚠️ Nicht prüfbar";
            }
        }

        private void AnalyzeLnkShortcut(DesktopItem item)
        {
            IShellLink? link = null;
            try
            {
                if (!File.Exists(item.FullPath))
                {
                    item.IsShortcutValid = false;
                    item.ShortcutStatus = "❌ Defekt";
                    item.ShortcutTarget = "Datei existiert nicht";
                    return;
                }

                link = (IShellLink)new ShellLink();
                var file = (IPersistFile)link;
                file.Load(item.FullPath, 0 /* STGM_READ */);

                var sb = new StringBuilder(1024);
                // 0x4 = SLGP_RAWPATH (verhindert Netzwerksuche / Hänger)
                link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0x4);
                string targetPath = sb.ToString();

                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    // Fallback ohne RAWPATH
                    link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                    targetPath = sb.ToString();
                }

                if (!string.IsNullOrWhiteSpace(targetPath))
                {
                    try
                    {
                        targetPath = Environment.ExpandEnvironmentVariables(targetPath).Trim();
                    }
                    catch { }
                }

                item.ShortcutTarget = targetPath;
                ValidateTargetPath(item, targetPath);
            }
            catch (Exception ex)
            {
                item.IsShortcutValid = false;
                item.ShortcutStatus = "⚠️ Nicht prüfbar";
                if (string.IsNullOrWhiteSpace(item.ShortcutTarget))
                {
                    item.ShortcutTarget = $"Nicht prüfbar ({ex.Message})";
                }
            }
            finally
            {
                if (link != null)
                {
                    try
                    {
                        Marshal.ReleaseComObject(link);
                    }
                    catch { }
                }
            }
        }

        private void ValidateTargetPath(DesktopItem item, string targetPath)
        {
            // 1. Leeres Ziel: Windows Apps / UWP (z.B. Paint, Rechner) oder Sonderlinks
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                item.ShortcutTarget = "(System- oder App-Verknüpfung)";
                item.IsShortcutValid = true;
                item.ShortcutStatus = "✔ OK (System)";
                return;
            }

            // 2. Benutzerdefinierte Protokolle / URLs (steam://, ms-settings:, http:// etc.)
            if (targetPath.Contains("://", StringComparison.OrdinalIgnoreCase) ||
                targetPath.StartsWith("ms-", StringComparison.OrdinalIgnoreCase) ||
                targetPath.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                item.IsShortcutValid = true;
                item.ShortcutStatus = "✔ OK (App/URL)";
                return;
            }

            // 3. Shell GUIDs (z.B. ::{20D04FE0-3AEA-1069-A2D8-08002B30309D} Dieser PC)
            if (targetPath.StartsWith("::{", StringComparison.OrdinalIgnoreCase))
            {
                item.IsShortcutValid = true;
                item.ShortcutStatus = "✔ OK (System)";
                return;
            }

            // 4. Netzwerkpfade (UNC: \\server\freigabe)
            if (targetPath.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (File.Exists(targetPath) || Directory.Exists(targetPath))
                    {
                        item.IsShortcutValid = true;
                        item.ShortcutStatus = "✔ OK (Netzwerk)";
                    }
                    else
                    {
                        item.IsShortcutValid = false;
                        item.ShortcutStatus = "❌ Defekt (Netzwerk)";
                    }
                }
                catch (Exception ex)
                {
                    item.IsShortcutValid = false;
                    item.ShortcutStatus = "⚠️ Nicht prüfbar";
                    item.ShortcutTarget = $"{targetPath} ({ex.Message})";
                }
                return;
            }

            // 5. Laufwerksprüfung (nicht vorhandenes oder getrenntes Laufwerk)
            try
            {
                string? root = Path.GetPathRoot(targetPath);
                if (!string.IsNullOrEmpty(root) && root.Length >= 2 && root[1] == ':')
                {
                    char driveLetter = char.ToUpperInvariant(root[0]);
                    var drive = DriveInfo.GetDrives().FirstOrDefault(d => char.ToUpperInvariant(d.Name[0]) == driveLetter);
                    if (drive == null || !drive.IsReady)
                    {
                        item.IsShortcutValid = false;
                        item.ShortcutStatus = "❌ Defekt (Laufwerk fehlt)";
                        return;
                    }
                }
            }
            catch
            {
                // Bei Problemen mit dem Pfadformat weiter zur normalen Prüfung
            }

            // 6. Lokale Datei- oder Ordnerprüfung
            try
            {
                if (File.Exists(targetPath) || Directory.Exists(targetPath))
                {
                    item.IsShortcutValid = true;
                    item.ShortcutStatus = "✔ OK";
                }
                else
                {
                    item.IsShortcutValid = false;
                    item.ShortcutStatus = "❌ Defekt";
                }
            }
            catch (Exception)
            {
                item.IsShortcutValid = false;
                item.ShortcutStatus = "⚠️ Nicht prüfbar";
            }
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        public class ShellLink
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        public interface IShellLink
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("0000010b-0000-0000-C000-000000000046")]
        public interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig]
            int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder ppszFileName);
        }
    }
}
