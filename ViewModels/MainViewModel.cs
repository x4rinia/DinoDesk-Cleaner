using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DinoDeskCleaner.Core.Action;
using DinoDeskCleaner.Core.Analyzer;
using DinoDeskCleaner.Core.Scanner;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using DinoDeskCleaner.Models;

namespace DinoDeskCleaner.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly DesktopScanner _scanner;
        private readonly ShortcutAnalyzer _shortcutAnalyzer;
        private readonly UsageAnalyzer _usageAnalyzer;
        private readonly FileMover _fileMover;
        private readonly FolderSuggester _suggester;
        private readonly RecycleBinManager _recycleBinManager;
        private readonly CleanupFolderAnalyzer _cleanupFolderAnalyzer;

        public MainViewModel()
        {
            _scanner = new DesktopScanner();
            _shortcutAnalyzer = new ShortcutAnalyzer();
            _usageAnalyzer = new UsageAnalyzer();
            _fileMover = new FileMover();
            _suggester = new FolderSuggester();
            _recycleBinManager = new RecycleBinManager();
            _cleanupFolderAnalyzer = new CleanupFolderAnalyzer();

            StatusMessage = "Bereit zum Scannen. Dino wartet!";

            // Papierkorbstatus initial beim Programmstart laden
            RefreshRecycleBin();

            // Dino-Symbol auf den Aufräumen-Ordner anwenden, falls er existiert
            try
            {
                string desktop = _scanner.GetDesktopPath();
                FolderIconManager.EnsureAufräumenDinoIcon(desktop);
            }
            catch { }
        }

        [ObservableProperty]
        private string statusMessage = "";

        [ObservableProperty]
        private string scanSummary = "";

        [ObservableProperty]
        private string recycleBinSummary = "Papierkorb wird ermittelt...";

        [ObservableProperty]
        private string recycleBinDetails = "";

        [ObservableProperty]
        private bool canEmptyRecycleBin;

        [ObservableProperty]
        private bool isRecycleBinEmptying;

        [ObservableProperty]
        private double recycleBinProgress;

        [ObservableProperty]
        private string recycleBinProgressText = "";

        [ObservableProperty]
        private DeletedItem? lastDeletedItem;

        [ObservableProperty]
        private bool hasLastDeletedItem;

        public bool HasNoLastDeletedItem => !HasLastDeletedItem;

        partial void OnHasLastDeletedItemChanged(bool value)
        {
            OnPropertyChanged(nameof(HasNoLastDeletedItem));
        }

        [ObservableProperty]
        private bool isBusy;

        [ObservableProperty]
        private int selectedTabIndex;

        partial void OnSelectedTabIndexChanged(int value)
        {
            // Wenn Tab 3 ("Papierkorb") geöffnet wird, Status automatisch neu laden
            if (value == 3)
            {
                RefreshRecycleBin();
            }
        }

        public ObservableCollection<DesktopItem> CleanupItems { get; } = new();
        public ObservableCollection<DesktopItem> ShortcutItems { get; } = new();
        public ObservableCollection<FolderSuggestion> FolderSuggestions { get; } = new();
        public ObservableCollection<string> ActionLog { get; } = new();
        public ObservableCollection<UsageAnalyzer.UsageData> MissingShortcuts { get; } = new();
        public ObservableCollection<CleanupCandidate> AufräumenCandidates { get; } = new();

        [ObservableProperty]
        private DesktopItem? selectedShortcutItem;

        [ObservableProperty]
        private string aufräumenCandidatesSummary = "Klicke auf 'Aufräumen-Ordner analysieren', um nach Doubletten & temporären Dateien zu suchen.";

        [ObservableProperty]
        private bool isAnalyzingAufräumen;

        [ObservableProperty]
        private bool hasCleanupItems;

        [ObservableProperty]
        private bool isDesktopClean = true;

        [RelayCommand]
        private async Task ScanDesktopAsync()
        {
            SelectedTabIndex = 0;
            IsBusy = true;
            StatusMessage = "Dino sucht normale Dateien und Ordner...";
            CleanupItems.Clear();

            await Task.Run(() =>
            {
                var path = _scanner.GetDesktopPath();
                var items = _scanner.ScanDesktop(path);

                // Sicherstellen, dass der Aufräumen-Ordner das Dino-Symbol trägt
                FolderIconManager.EnsureAufräumenDinoIcon(path);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var item in items) CleanupItems.Add(item);

                    int filesCount = items.Count(i => i.Category != ItemCategory.Ordner);
                    int foldersCount = items.Count(i => i.Category == ItemCategory.Ordner);

                    ScanSummary = $"{filesCount} Aufräum-Dateien gefunden.\n{foldersCount} Ordner gefunden.";
                    HasCleanupItems = CleanupItems.Count > 0;
                    IsDesktopClean = CleanupItems.Count == 0;
                    StatusMessage = "Dateien & Ordner analysiert.";
                });
            });

            IsBusy = false;
        }

        [RelayCommand]
        private void SelectAllCleanupItems()
        {
            foreach (var item in CleanupItems)
            {
                item.IsSelected = true;
            }
        }

        [RelayCommand]
        private void DeselectAllCleanupItems()
        {
            foreach (var item in CleanupItems)
            {
                item.IsSelected = false;
            }
        }

        [RelayCommand]
        private async Task CheckShortcutsAsync()
        {
            SelectedTabIndex = 1;
            IsBusy = true;
            StatusMessage = "Dino prüft Verknüpfungen...";
            ShortcutItems.Clear();
            MissingShortcuts.Clear();
            SelectedShortcutItem = null;

            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        var path = _scanner.GetDesktopPath();
                        var items = _scanner.ScanShortcuts(path);

                        foreach (var item in items)
                        {
                            try
                            {
                                _shortcutAnalyzer.AnalyzeShortcut(item);
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

                            try
                            {
                                _usageAnalyzer.AnalyzeUsage(item);
                            }
                            catch
                            {
                                // Einzelne Nutzungsanalyse darf die Prüfung niemals abbrechen
                            }
                        }

                        // Sortieren nach Score (hohe Nutzung oben)
                        try
                        {
                            items = items.OrderByDescending(i => i.UsageScore).ToList();
                        }
                        catch { }

                        List<UsageAnalyzer.UsageData> missing = new();
                        try
                        {
                            missing = _usageAnalyzer.GetTopUsedProgramsNotInList(items.Select(i => i.Name), 6);
                        }
                        catch
                        {
                            missing = new List<UsageAnalyzer.UsageData>();
                        }

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                foreach (var item in items) ShortcutItems.Add(item);
                                foreach (var m in missing) MissingShortcuts.Add(m);

                                int brokenShortcuts = items.Count(i => !i.IsShortcutValid);
                                int uncheckable = items.Count(i => i.ShortcutStatus.Contains("Nicht prüfbar"));
                                string detail = brokenShortcuts > 0 ? $" ({brokenShortcuts} defekt{(uncheckable > 0 ? $", {uncheckable} nicht prüfbar" : "")})" : "";
                                StatusMessage = $"{items.Count} Verknüpfungen geprüft{detail}.";
                                ActionLog.Add($"[{DateTime.Now:HH:mm}] {items.Count} Verknüpfungen geprüft{detail}.");
                            }
                            catch (Exception ex)
                            {
                                StatusMessage = $"Fehler bei UI-Aktualisierung: {ex.Message}";
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            StatusMessage = $"Fehler beim Prüfen der Verknüpfungen: {ex.Message}";
                            ActionLog.Add($"[{DateTime.Now:HH:mm}] Fehler bei Verknüpfungsprüfung: {ex.Message}");
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Fehler: {ex.Message}";
                ActionLog.Add($"[{DateTime.Now:HH:mm}] Fehler: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public void SelectShortcutForAction(object parameter)
        {
            if (parameter is not DesktopItem item) return;
            foreach (var s in ShortcutItems)
            {
                s.IsSelectedForAction = (s == item);
            }
            SelectedShortcutItem = item;
        }

        [RelayCommand]
        private void DeleteSelectedShortcut()
        {
            if (SelectedShortcutItem == null)
            {
                MessageBox.Show("Bitte wähle etwas aus.", "DinoDesk", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"Möchtest du die Verknüpfung '{SelectedShortcutItem.Name}' wirklich vom Desktop in den Papierkorb verschieben?",
                "Verknüpfung löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                string targetFile = SelectedShortcutItem.FullPath;
                if (RecycleBinManager.SendToRecycleBin(targetFile))
                {
                    ActionLog.Add($"[{DateTime.Now:HH:mm}] Verknüpfung '{SelectedShortcutItem.Name}' in den Papierkorb verschoben.");
                    StatusMessage = $"Verknüpfung '{SelectedShortcutItem.Name}' gelöscht.";
                    ShortcutItems.Remove(SelectedShortcutItem);
                    SelectedShortcutItem = null;
                    RefreshRecycleBin();
                }
                else
                {
                    MessageBox.Show("Die Verknüpfung konnte nicht gelöscht werden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private void OpenSelectedShortcutTarget()
        {
            if (SelectedShortcutItem == null)
            {
                MessageBox.Show("Bitte wähle etwas aus.", "DinoDesk", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string target = SelectedShortcutItem.ShortcutTarget?.Trim() ?? string.Empty;
            string lnkPath = SelectedShortcutItem.FullPath;

            try
            {
                // 1. Falls es ein Steam-Link ist (z.B. steam://rungameid/2300320)
                if (target.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
                {
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                    return;
                }

                // 2. Falls die Zieldatei direkt existiert
                if (!string.IsNullOrEmpty(target) && File.Exists(target))
                {
                    Process.Start("explorer.exe", $"/select,\"{target}\"");
                    return;
                }

                // 3. Falls das Ziel ein Ordner ist
                if (!string.IsNullOrEmpty(target) && Directory.Exists(target))
                {
                    Process.Start("explorer.exe", $"\"{target}\"");
                    return;
                }

                // 4. Falls der übergeordnete Ordner existiert
                if (!string.IsNullOrEmpty(target))
                {
                    string? parent = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    {
                        Process.Start("explorer.exe", $"\"{parent}\"");
                        return;
                    }
                }

                // 5. Fallback: Verknüpfungsdatei selbst auf dem Desktop markieren
                if (File.Exists(lnkPath))
                {
                    Process.Start("explorer.exe", $"/select,\"{lnkPath}\"");
                    return;
                }

                MessageBox.Show($"Zielpfad konnte nicht ermittelt werden:\n{target}", "DinoDesk", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Öffnen: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private async Task SearchAppInWindowsAsync()
        {
            if (SelectedShortcutItem == null)
            {
                MessageBox.Show("Bitte wähle etwas aus.", "DinoDesk", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string appName = SelectedShortcutItem.Name;
            if (appName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                appName = appName.Substring(0, appName.Length - 4);
            else if (appName.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
                appName = appName.Substring(0, appName.Length - 4);
            
            appName = appName.Trim();

            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });

                bool automationSuccess = false;
                await Task.Run(async () =>
                {
                    for (int i = 0; i < 5; i++)
                    {
                        await Task.Delay(1500); // 1.5s warten pro Iteration
                        try
                        {
                            var root = System.Windows.Automation.AutomationElement.RootElement;
                            var topLevelWindows = root.FindAll(System.Windows.Automation.TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);
                            
                            foreach (System.Windows.Automation.AutomationElement window in topLevelWindows)
                            {
                                // Überspringe leere Fenster oder DinoDesk selbst
                                if (string.IsNullOrEmpty(window.Current.Name) || window.Current.Name.Contains("DinoDesk"))
                                    continue;

                                // Wir suchen direkt nach dem Suchfeld
                                var searchBox = window.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                                    new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.AutomationIdProperty, "SystemSettings_AppListSearch_InputButton"));
                                
                                if (searchBox == null)
                                {
                                    searchBox = window.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                                        new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, "Apps durchsuchen"));
                                }

                                if (searchBox == null)
                                {
                                    searchBox = window.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                                        new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.AutomationIdProperty, "SearchTextBox"));
                                }

                                if (searchBox != null)
                                {
                                    searchBox.SetFocus();
                                    
                                    if (searchBox.GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern) is System.Windows.Automation.ValuePattern valuePattern)
                                    {
                                        valuePattern.SetValue(appName);
                                    }
                                    else
                                    {
                                        // Fallback via SendKeys
                                        System.Windows.Forms.SendKeys.SendWait("^{a}{DELETE}"); // Alles markieren und löschen
                                        System.Windows.Forms.SendKeys.SendWait(appName);
                                    }
                                    
                                    automationSuccess = true;
                                    return;
                                }
                            }
                        }
                        catch { }
                    }
                });

                if (!automationSuccess)
                {
                    Clipboard.SetText(appName);
                    StatusMessage = "Windows wurde geöffnet. Der Programmname konnte nicht automatisch eingetragen werden und wurde in die Zwischenablage kopiert.";
                    MessageBox.Show("Windows wurde geöffnet. Der Programmname konnte nicht automatisch eingetragen werden und wurde in die Zwischenablage kopiert.", "DinoDesk", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusMessage = $"Suche nach '{appName}' in Windows Apps gestartet.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Öffnen der Einstellungen: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void CreateMissingShortcut(object parameter)
        {
            if (parameter is not UsageAnalyzer.UsageData app || string.IsNullOrWhiteSpace(app.ExecutablePath))
            {
                MessageBox.Show("Keine Programminformationen vorhanden.", "DinoDesk", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string targetPath = UsageAnalyzer.ResolveKnownFolder(app.ExecutablePath);

            ShortcutAnalyzer.IShellLink? link = null;
            try
            {
                link = (ShortcutAnalyzer.IShellLink)new ShortcutAnalyzer.ShellLink();
                string desktop = _scanner.GetDesktopPath();
                string shortcutPath = Path.Combine(desktop, $"{app.Name}.lnk");
                
                link.SetPath(targetPath);
                if (File.Exists(targetPath))
                {
                    link.SetWorkingDirectory(Path.GetDirectoryName(targetPath) ?? "");
                }
                link.SetDescription($"Verknüpfung zu {app.Name}");

                var file = (ShortcutAnalyzer.IPersistFile)link;
                file.Save(shortcutPath, false);

                ActionLog.Add($"[{DateTime.Now:HH:mm}] Desktop-Verknüpfung für '{app.Name}' erstellt.");
                StatusMessage = $"Verknüpfung für '{app.Name}' auf dem Desktop angelegt!";
                _ = CheckShortcutsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Erstellen der Verknüpfung: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (link != null)
                {
                    try { System.Runtime.InteropServices.Marshal.ReleaseComObject(link); } catch { }
                }
            }
        }

        [RelayCommand]
        private void CreateSuggestions()
        {
            SelectedTabIndex = 2;
            FolderSuggestions.Clear();
            var path = _scanner.GetDesktopPath();
            var items = _scanner.ScanShortcuts(path);
            var suggestions = _suggester.SuggestFolders(items.ToList());
            foreach (var s in suggestions)
            {
                FolderSuggestions.Add(s);
            }
            StatusMessage = $"Dino hat {suggestions.Count} thematische Ordner-Vorschläge erstellt (keine Duplikate).";
        }

        [RelayCommand]
        private void ApplyFolderSuggestions()
        {
            if (!FolderSuggestions.Any())
            {
                StatusMessage = "Keine Vorschläge vorhanden.";
                return;
            }

            var path = _scanner.GetDesktopPath();
            int moved = _fileMover.ApplyFolderSuggestions(path, FolderSuggestions.ToList());
            ActionLog.Add($"[{DateTime.Now:HH:mm}] Dino hat {moved} Verknüpfungen in Themen-Ordner einsortiert.");
            StatusMessage = $"{moved} Verknüpfungen wurden in neue Ordner verschoben.";
            FolderSuggestions.Clear();
            _ = CheckShortcutsAsync();
        }

        [RelayCommand]
        private void ApplyCleanup()
        {
            StatusMessage = "Dino räumt den Desktop auf...";
            var path = _scanner.GetDesktopPath();

            var selectedToCleanup = CleanupItems.Where(i => i.IsSelected).ToList();
            int movedFiles = 0;
            int movedFolders = 0;

            if (selectedToCleanup.Any())
            {
                movedFiles = _fileMover.CreateCleanupFolderAndMove(path, selectedToCleanup.Where(i => i.Category != ItemCategory.Ordner).ToList());
                movedFolders = _fileMover.CreateCleanupFolderAndMove(path, selectedToCleanup.Where(i => i.Category == ItemCategory.Ordner).ToList());
            }

            string dateStr = DateTime.Now.ToString("yyyy-MM-dd");
            ActionLog.Clear();
            ActionLog.Add($"[{DateTime.Now:HH:mm}] Dino hat aufgeräumt:");
            ActionLog.Add($" - {movedFiles} Dateien verschoben (Ziel: Aufräumen\\{dateStr})");
            ActionLog.Add($" - {movedFolders} Ordner verschoben (Ziel: Aufräumen\\{dateStr}\\Ordner)");

            StatusMessage = "Aufräumen abgeschlossen! Siehe Protokoll für Details.";

            _ = ScanDesktopAsync();
            SelectedTabIndex = 4;
        }

        [RelayCommand]
        private void OpenProtokoll()
        {
            SelectedTabIndex = 4;
        }

        [RelayCommand]
        private void ClearProtokoll()
        {
            ActionLog.Clear();
        }

        [RelayCommand]
        public void RefreshRecycleBin()
        {
            var info = _recycleBinManager.GetRecycleBinInfo();
            RecycleBinSummary = info.DisplaySummary;
            CanEmptyRecycleBin = info.ItemCount > 0;

            if (info.DriveDetails.Count > 0)
            {
                RecycleBinDetails = "Gefundene Laufwerke:\n" + string.Join("\n", info.DriveDetails);
            }
            else
            {
                RecycleBinDetails = string.Empty;
            }

            LastDeletedItem = _recycleBinManager.GetLastDeletedItem();
            HasLastDeletedItem = LastDeletedItem != null;
        }

        [RelayCommand]
        private void CheckRecycleBin()
        {
            SelectedTabIndex = 3;
            RefreshRecycleBin();
        }

        [RelayCommand]
        private async Task EmptyRecycleBinAsync()
        {
            var info = _recycleBinManager.GetRecycleBinInfo();
            if (info.ItemCount == 0)
            {
                CanEmptyRecycleBin = false;
                RecycleBinSummary = "Papierkorb ist leer (0 Elemente).";
                return;
            }

            // DinoDesk-eigene Bestätigung anzeigen
            var confirmation = MessageBox.Show(
                $"Dino hat {info.ItemCount} Elemente mit insgesamt {info.FormattedSize} im Papierkorb gefunden.\n\nWirklich endgültig löschen?",
                "DinoDesk - Papierkorb leeren",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            IsBusy = true;
            IsRecycleBinEmptying = true;
            RecycleBinProgress = 5;
            RecycleBinProgressText = "Papierkorb wird vorbereitet...";
            StatusMessage = "Papierkorb wird geleert...";

            var progressReporter = new Progress<(double percent, string text)>(p =>
            {
                RecycleBinProgress = p.percent;
                RecycleBinProgressText = p.text;
                StatusMessage = p.text;
            });

            var result = await Task.Run(() => _recycleBinManager.EmptyRecycleBin(progressReporter));

            RefreshRecycleBin();
            IsBusy = false;
            IsRecycleBinEmptying = false;

            if (result.Success)
            {
                ActionLog.Add($"[{DateTime.Now:HH:mm}] Papierkorb erfolgreich geleert.");
                StatusMessage = "Papierkorb wurde erfolgreich geleert.";
            }
            else
            {
                RecycleBinSummary = "Dino konnte den Papierkorb nicht vollständig leeren.";
                RecycleBinDetails = $"{result.ErrorMessage}\nVerbleibend: {result.RemainingItems} Elemente ({result.RemainingSizeFormatted})";
                StatusMessage = "Papierkorb konnte nicht vollständig geleert werden.";
                ActionLog.Add($"[{DateTime.Now:HH:mm}] Fehler beim Leeren des Papierkorbs: {result.ErrorMessage}");
            }
        }

        [RelayCommand]
        private async Task RestoreLastDeletedItemAsync()
        {
            if (LastDeletedItem == null) return;

            string name = LastDeletedItem.Name;
            var confirm = MessageBox.Show($"Möchtest du '{name}' wirklich an seinen ursprünglichen Ort wiederherstellen?\n\n{LastDeletedItem.OriginalLocation}", 
                "Wiederherstellen", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            IsBusy = true;
            StatusMessage = $"Stelle '{name}' wieder her...";

            bool success = false;
            string error = "";

            await Task.Run(() =>
            {
                success = _recycleBinManager.RestoreItem(LastDeletedItem, out error);
            });

            if (success)
            {
                StatusMessage = $"Dino hat '{name}' wiederhergestellt.";
                MessageBox.Show($"Dino hat '{name}' wiederhergestellt.", "Wiederhergestellt", MessageBoxButton.OK, MessageBoxImage.Information);
                ActionLog.Add($"[{DateTime.Now:HH:mm}] Element '{name}' aus dem Papierkorb wiederhergestellt.");
            }
            else
            {
                StatusMessage = $"Fehler beim Wiederherstellen von '{name}'.";
                MessageBox.Show($"Dino konnte dieses Element nicht wiederherstellen.\n\nUrsache: {error}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
                ActionLog.Add($"[{DateTime.Now:HH:mm}] Fehler bei Wiederherstellung von '{name}': {error}");
            }

            IsBusy = false;
            RefreshRecycleBin();
        }

        [RelayCommand]
        private async Task AnalyzeAufräumenAsync()
        {
            IsAnalyzingAufräumen = true;
            StatusMessage = "Dino durchsucht den Ordner 'Aufräumen' nach Doubletten & Müll...";
            AufräumenCandidates.Clear();

            await Task.Run(() =>
            {
                string desktop = _scanner.GetDesktopPath();
                string aufräumenRoot = Path.Combine(desktop, "Aufräumen");
                var list = _cleanupFolderAnalyzer.AnalyzeCleanupFolder(aufräumenRoot);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var item in list)
                    {
                        AufräumenCandidates.Add(item);
                    }

                    long totalBytes = list.Sum(i => i.SizeInBytes);
                    AufräumenCandidatesSummary = list.Count > 0
                        ? $"Dino hat {list.Count} Doubletten / temporäre Dateien gefunden ({RecycleBinManager.FormatBytes(totalBytes)}). Wähle aus, was in den Papierkorb soll:"
                        : "Keine überflüssigen Doubletten oder temporären Dateien im Aufräumen-Ordner gefunden!";
                    StatusMessage = $"Aufräumen-Ordner analysiert: {list.Count} Funde.";
                });
            });

            IsAnalyzingAufräumen = false;
        }

        [RelayCommand]
        private void SelectAllCandidates()
        {
            foreach (var c in AufräumenCandidates)
            {
                c.IsSelected = true;
            }
        }

        [RelayCommand]
        private void DeselectAllCandidates()
        {
            foreach (var c in AufräumenCandidates)
            {
                c.IsSelected = false;
            }
        }

        [RelayCommand]
        private async Task MoveSelectedCandidatesToRecycleBinAsync()
        {
            var selected = AufräumenCandidates.Where(c => c.IsSelected).ToList();
            if (!selected.Any())
            {
                MessageBox.Show("Keine Elemente ausgewählt.", "DinoDesk", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            long totalBytes = selected.Sum(s => s.SizeInBytes);
            var confirm = MessageBox.Show(
                $"Möchtest du {selected.Count} ausgewählte Dateien ({RecycleBinManager.FormatBytes(totalBytes)}) wirklich in den Papierkorb verschieben?",
                "In den Papierkorb verschieben",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            IsBusy = true;
            StatusMessage = "Dateien werden in den Papierkorb gelegt...";

            int movedCount = 0;
            await Task.Run(() =>
            {
                foreach (var item in selected)
                {
                    if (RecycleBinManager.SendToRecycleBin(item.FullPath))
                    {
                        movedCount++;
                        Application.Current.Dispatcher.Invoke(() => AufräumenCandidates.Remove(item));
                    }
                }
            });

            RefreshRecycleBin();
            IsBusy = false;

            ActionLog.Add($"[{DateTime.Now:HH:mm}] Dino hat {movedCount} Doubletten/Dateien aus 'Aufräumen' in den Papierkorb gelegt.");
            StatusMessage = $"{movedCount} Dateien in den Papierkorb gelegt.";
        }
    }
}
