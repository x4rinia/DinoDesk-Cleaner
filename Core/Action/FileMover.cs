using System;
using System.Collections.Generic;
using System.IO;
using DinoDeskCleaner.Models;

namespace DinoDeskCleaner.Core.Action
{
    public class FileMover
    {
        public string GetSafeDestinationPath(string targetDir, string fileName)
        {
            string dest = Path.Combine(targetDir, fileName);
            int count = 1;
            string nameOnly = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);

            while (File.Exists(dest) || Directory.Exists(dest))
            {
                string tempFileName = $"{nameOnly} ({count}){ext}";
                dest = Path.Combine(targetDir, tempFileName);
                count++;
            }
            return dest;
        }

        public bool MoveItemToFolder(DesktopItem item, string targetFolder)
        {
            try
            {
                if (!Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }

                string destPath = GetSafeDestinationPath(targetFolder, item.Name);
                
                if (item.Category == ItemCategory.Ordner)
                {
                    Directory.Move(item.FullPath, destPath);
                }
                else
                {
                    File.Move(item.FullPath, destPath);
                }
                
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        public int CreateCleanupFolderAndMove(string desktopPath, List<DesktopItem> itemsToMove)
        {
            if (itemsToMove.Count == 0) return 0;

            string cleanupDir = Path.Combine(desktopPath, "Aufräumen");
            FolderIconManager.SetDinoIcon(cleanupDir);

            string dateDir = Path.Combine(cleanupDir, DateTime.Now.ToString("yyyy-MM-dd"));
            
            if (!Directory.Exists(dateDir))
            {
                Directory.CreateDirectory(dateDir);
            }
            
            int movedCount = 0;
            foreach (var item in itemsToMove)
            {
                string categoryFolder = item.Category.ToString();
                if (item.Category == ItemCategory.Ordner)
                {
                    categoryFolder = "Ordner";
                }
                
                string targetCategoryDir = Path.Combine(dateDir, categoryFolder);
                if (MoveItemToFolder(item, targetCategoryDir))
                {
                    movedCount++;
                }
            }
            return movedCount;
        }

        public int ApplyFolderSuggestions(string desktopPath, List<FolderSuggestion> suggestions)
        {
            int movedCount = 0;
            foreach (var suggestion in suggestions)
            {
                string targetDir = Path.Combine(desktopPath, suggestion.SuggestedName);
                foreach (var item in suggestion.Items)
                {
                    if (item.IsSelected)
                    {
                        if (MoveItemToFolder(item, targetDir))
                        {
                            movedCount++;
                        }
                    }
                }
            }
            return movedCount;
        }
    }
}
