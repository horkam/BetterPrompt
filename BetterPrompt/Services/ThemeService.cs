using BetterPrompt.Models;
using Microsoft.Win32;
using System.Windows;

namespace BetterPrompt.Services;

public static class ThemeService
{
    private static readonly Uri DarkUri  = new("Themes/Dark.xaml",  UriKind.Relative);
    private static readonly Uri LightUri = new("Themes/Light.xaml", UriKind.Relative);

    public static void Apply(AppTheme theme)
    {
        var resolved = theme == AppTheme.System ? DetectSystemTheme() : theme;
        var uri = resolved == AppTheme.Light ? LightUri : DarkUri;

        var dicts = Application.Current.Resources.MergedDictionaries;
        dicts.Clear();
        dicts.Add(new ResourceDictionary { Source = uri });
    }

    public static AppTheme DetectSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int val && val == 1)
                return AppTheme.Light;
        }
        catch { }
        return AppTheme.Dark;
    }
}
