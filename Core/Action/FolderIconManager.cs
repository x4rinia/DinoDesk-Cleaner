using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DinoDeskCleaner.Core.Action
{
    public static class FolderIconManager
    {
        [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const int SHCNE_UPDATEDIR = 0x00001000;
        private const uint SHCNF_FLUSH = 0x1000;

        public static void EnsureAufräumenDinoIcon(string desktopPath)
        {
            string folderPath = Path.Combine(desktopPath, "Aufräumen");
            SetDinoIcon(folderPath);
        }

        public static void SetDinoIcon(string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                // 1. Icon-Datei kopieren
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string sourceIco = Path.Combine(appDir, "Assets", "folder_dino.ico");
                if (!File.Exists(sourceIco))
                {
                    sourceIco = Path.Combine(appDir, "folder_dino.ico");
                }
                if (!File.Exists(sourceIco))
                {
                    sourceIco = @"H:\DinoDesk Cleaner\Assets\folder_dino.ico";
                }

                if (File.Exists(sourceIco))
                {
                    string targetIco = Path.Combine(folderPath, "dino.ico");
                    if (File.Exists(targetIco))
                    {
                        try { File.SetAttributes(targetIco, FileAttributes.Normal); } catch { }
                    }
                    File.Copy(sourceIco, targetIco, true);
                    try { File.SetAttributes(targetIco, FileAttributes.Hidden | FileAttributes.System); } catch { }
                }

                // 2. desktop.ini schreiben (mit relativem Pfad dino.ico,0)
                string desktopIni = Path.Combine(folderPath, "desktop.ini");
                if (File.Exists(desktopIni))
                {
                    try { File.SetAttributes(desktopIni, FileAttributes.Normal); } catch { }
                }

                string content = "[.ShellClassInfo]\r\nIconResource=dino.ico,0\r\nIconFile=dino.ico\r\nIconIndex=0\r\n[ViewState]\r\nMode=\r\nVid=\r\nFolderType=Generic\r\n";
                File.WriteAllText(desktopIni, content, Encoding.Unicode);

                // desktop.ini muss Hidden + System sein
                try { File.SetAttributes(desktopIni, FileAttributes.Hidden | FileAttributes.System); } catch { }

                // Ordner selbst MUSS ReadOnly sein, damit Windows Explorer die desktop.ini auswertet!
                var dirInfo = new DirectoryInfo(folderPath);
                dirInfo.Attributes |= FileAttributes.ReadOnly;

                // Windows Explorer benachrichtigen
                SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
            }
        }
    }
}
