using BetterPrompt.Services;
using System.Windows;

namespace BetterPrompt;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Apply saved theme before the main window is created
        var settings = new SettingsService().Load();
        ThemeService.Apply(settings.Theme);
    }
}
