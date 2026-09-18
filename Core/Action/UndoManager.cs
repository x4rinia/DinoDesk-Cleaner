using System;
using System.Collections.Generic;
using System.IO;

namespace DinoDeskCleaner.Core.Action
{
    public class UndoAction
    {
        public string OriginalPath { get; set; } = string.Empty;
        public string NewPath { get; set; } = string.Empty;
    }

    public class UndoManager
    {
        private readonly List<UndoAction> _history = new();

        public void AddAction(string originalPath, string newPath)
        {
            _history.Add(new UndoAction { OriginalPath = originalPath, NewPath = newPath });
        }

        public List<UndoAction> GetHistory() => _history;

        public void Clear() => _history.Clear();

        public int UndoAll()
        {
            int restoredCount = 0;
            // Rückwärts abarbeiten, um umgekehrte Reihenfolge sicherzustellen
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                var action = _history[i];
                try
                {
                    if (File.Exists(action.NewPath))
                    {
                        string? parent = Path.GetDirectoryName(action.OriginalPath);
                        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                        {
                            Directory.CreateDirectory(parent);
                        }

                        // Falls am Originalpfad bereits eine Datei existiert, nicht überschreiben
                        if (!File.Exists(action.OriginalPath))
                        {
                            File.Move(action.NewPath, action.OriginalPath);
                            restoredCount++;
                        }
                    }
                    else if (Directory.Exists(action.NewPath))
                    {
                        string? parent = Path.GetDirectoryName(action.OriginalPath);
                        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                        {
                            Directory.CreateDirectory(parent);
                        }

                        if (!Directory.Exists(action.OriginalPath))
                        {
                            Directory.Move(action.NewPath, action.OriginalPath);
                            restoredCount++;
                        }
                    }
                }
                catch
                {
                    // Fehler bei einzelnen Dateien nicht zum Komplettabsturz führen
                }
            }
            Clear();
            return restoredCount;
        }
    }
}
