using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace DinoDeskCleaner
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. UI Thread Exceptions abfangen (Verhindert App-Absturz bei UI-Fehlern)
            DispatcherUnhandledException += (s, args) =>
            {
                args.Handled = true;
                LogError("DispatcherUnhandledException", args.Exception);
                MessageBox.Show(
                    $"Ein unerwarteter Fehler ist aufgetreten:\n\n{args.Exception.Message}\n\nDie Anwendung läuft weiter.",
                    "DinoDesk Cleaner - Hinweis",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            };

            // 2. Hintergrund-Task Exceptions
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                args.SetObserved();
                LogError("UnobservedTaskException", args.Exception);
            };

            // 3. AppDomain-weite Exceptions
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    LogError("AppDomain.UnhandledException", ex);
                }
            };
        }

        private static void LogError(string context, Exception ex)
        {
            try
            {
                string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DinoDeskCleaner");
                Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, "crash_log.txt");
                File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{context}] {ex}\n\n");
            }
            catch
            {
                // Protokollierungsfehler ignorieren
            }
        }
    }
}
