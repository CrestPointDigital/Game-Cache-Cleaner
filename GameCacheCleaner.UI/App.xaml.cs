using System;
using System.IO;

namespace GameCacheCleaner.UI
{
    public partial class App : System.Windows.Application
    {
        private string LogDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrestPoint", "GCC", "logs");
        private string LogPath => Path.Combine(LogDir, "app.log");

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                File.AppendAllText(LogPath, $"[Startup] {DateTime.UtcNow:O} args='{string.Join(" ", e.Args)}'\n");
            }
            catch { }

            this.DispatcherUnhandledException += (s, exArgs) =>
            {
                try { File.AppendAllText(LogPath, $"[Unhandled] {DateTime.UtcNow:O} {exArgs.Exception}\n"); } catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, exArgs) =>
            {
                try { File.AppendAllText(LogPath, $"[AppDomain] {DateTime.UtcNow:O} {exArgs.ExceptionObject}\n"); } catch { }
            };

            base.OnStartup(e);
        }
    }
}
