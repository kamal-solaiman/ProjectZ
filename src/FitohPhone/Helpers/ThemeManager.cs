using System.Windows;

namespace FitohPhone.Helpers;

/// <summary>
/// Pure UI helper to switch between the 8 visual XAML ResourceDictionary themes at design/preview time.
/// </summary>
public static class ThemeManager
{
    public static string CurrentThemeKey { get; private set; } = "Glassmorphism";

    public static event Action<string>? ThemeChanged;

    public static void ApplyTheme(string themeKey)
    {
        string file = themeKey switch
        {
            "LiquidGlass" => "LiquidGlassTheme.xaml",
            "DarkGlass" => "DarkGlassTheme.xaml",
            "Aurora" => "AuroraTheme.xaml",
            "Holographic" => "HolographicTheme.xaml",
            "FuiHud" => "FuiHudTheme.xaml",
            "ChromeMetal" => "ChromeMetalTheme.xaml",
            "Fluent" => "FluentTheme.xaml",
            _ => "GlassmorphismTheme.xaml"
        };

        var rootDicts = Application.Current?.Resources?.MergedDictionaries;
        if (rootDicts is null) return;

        var themeUri = new Uri($"pack://application:,,,/Resources/Themes/{file}", UriKind.Absolute);
        var newTheme = new ResourceDictionary { Source = themeUri };

        if (rootDicts.Count > 0)
            rootDicts[0] = newTheme;
        else
            rootDicts.Insert(0, newTheme);

        CurrentThemeKey = themeKey;
        ThemeChanged?.Invoke(themeKey);
    }
}
