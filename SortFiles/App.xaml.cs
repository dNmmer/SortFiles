using Microsoft.Win32;
using System;
using System.Windows;

namespace SortFiles;

public partial class App : System.Windows.Application
{
    public static bool IsLightTheme { get; private set; } = true;
    public static event Action? ThemeChanged;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplySystemTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        base.OnExit(e);
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            Dispatcher.Invoke(ApplySystemTheme);
        }
    }

    private void ApplySystemTheme()
    {
        IsLightTheme = DetectLightTheme();
        var uri = new Uri(IsLightTheme ? "Themes/LightTheme.xaml" : "Themes/DarkTheme.xaml", UriKind.Relative);
        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
        ThemeChanged?.Invoke();
    }

    private static bool DetectLightTheme()
    {
        try
        {
            const string keyPath = @"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize";
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            if (key?.GetValue("AppsUseLightTheme") is int lightValue)
            {
                return lightValue > 0;
            }
        }
        catch
        {
        }

        return true;
    }
}
