using BetterPrompt.Services;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace BetterPrompt;

public partial class App : Application
{
    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "BetterPrompt_crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Log("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
            MessageBox.Show(args.Exception.ToString(), "Unhandled Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log("AppDomain.UnhandledException", args.ExceptionObject as Exception);

        var settings = new SettingsService().Load();
        ThemeService.Apply(settings.Theme);
    }

    private static void Log(string source, Exception? ex)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:HH:mm:ss}] {source}:\n{ex}\n\n");
        }
        catch { }
    }
}
