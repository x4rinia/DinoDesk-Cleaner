using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DinoDeskCleaner.Core.Action
{
    public class LockedItemEntry
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public DateTime LockedAt { get; set; } = DateTime.Now;
    }

    public class LockedItemsManager
    {
        private readonly string _filePath;
        private readonly object _lockObj = new();
        private readonly Dictionary<string, LockedItemEntry> _itemsByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, LockedItemEntry> _itemsByPath = new(StringComparer.OrdinalIgnoreCase);

        public LockedItemsManager()
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DinoDeskCleaner");
            _filePath = Path.Combine(appData, "locked_items.json");
            Load();
        }

        public bool IsLocked(string name, string fullPath)
        {
            lock (_lockObj)
            {
                if (!string.IsNullOrWhiteSpace(name) && _itemsByName.ContainsKey(name))
                    return true;
                if (!string.IsNullOrWhiteSpace(fullPath) && _itemsByPath.ContainsKey(fullPath))
                    return true;
                return false;
            }
        }

        public void Lock(string name, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(fullPath)) return;
            string itemName = !string.IsNullOrWhiteSpace(name) ? name : Path.GetFileName(fullPath);

            lock (_lockObj)
            {
                var entry = new LockedItemEntry
                {
                    Name = itemName,
                    FullPath = fullPath ?? string.Empty,
                    LockedAt = DateTime.Now
                };

                _itemsByName[itemName] = entry;
                if (!string.IsNullOrWhiteSpace(fullPath))
                {
                    _itemsByPath[fullPath] = entry;
                }
                Save();
            }
        }

        public void Unlock(string name, string fullPath)
        {
            lock (_lockObj)
            {
                bool changed = false;

                if (!string.IsNullOrWhiteSpace(name) && _itemsByName.Remove(name))
                {
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(fullPath) && _itemsByPath.Remove(fullPath))
                {
                    changed = true;
                }

                var toRemove = _itemsByName.Values
                    .Where(e => (!string.IsNullOrEmpty(name) && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrEmpty(fullPath) && string.Equals(e.FullPath, fullPath, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                foreach (var entry in toRemove)
                {
                    _itemsByName.Remove(entry.Name);
                    if (!string.IsNullOrEmpty(entry.FullPath))
                    {
                        _itemsByPath.Remove(entry.FullPath);
                    }
                    changed = true;
                }

                if (changed)
                {
                    Save();
                }
            }
        }

        public int GetLockedCount()
        {
            lock (_lockObj)
            {
                return _itemsByName.Count;
            }
        }

        public IReadOnlyList<LockedItemEntry> GetAllLockedItems()
        {
            lock (_lockObj)
            {
                return _itemsByName.Values.OrderBy(i => i.Name).ToList();
            }
        }

        private void Load()
        {
            lock (_lockObj)
            {
                try
                {
                    if (!File.Exists(_filePath)) return;
                    string json = File.ReadAllText(_filePath);
                    var list = JsonSerializer.Deserialize<List<LockedItemEntry>>(json);
                    if (list != null)
                    {
                        _itemsByName.Clear();
                        _itemsByPath.Clear();
                        foreach (var item in list)
                        {
                            if (!string.IsNullOrWhiteSpace(item.Name))
                                _itemsByName[item.Name] = item;
                            if (!string.IsNullOrWhiteSpace(item.FullPath))
                                _itemsByPath[item.FullPath] = item;
                        }
                    }
                }
                catch
                {
                    // Fallback bei fehlerhafter Datei
                }
            }
        }

        private void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(_filePath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var list = _itemsByName.Values.ToList();
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(list, options);
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // Fehler beim Speichern ignorieren
            }
        }
    }
}
