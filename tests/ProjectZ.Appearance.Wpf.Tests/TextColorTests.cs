using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Tests;

/// <summary>
/// Regression probes for the reported "text colors never change" symptom. These assert the
/// *effective* WPF Foreground on real controls/styles, not merely the resource dictionary values.
/// </summary>
internal static class TextColorTests
{
    public static void Run(Action<bool, string> check)
    {
        var runtime = ThemeManager.Instance;
        var probe = new Window { Width = 520, Height = 360 };
        var host = new Grid();
        probe.Content = host;
        var plain = new TextBlock { Text = "نص بلا نمط" };
        var body = new TextBlock { Text = "نص أساسي", Style = (Style)Application.Current.Resources["PosRtlTextStyle"] };
        var heading = new TextBlock { Text = "عنوان", Style = (Style)Application.Current.Resources["ThemeSectionTitle"] };
        var button = new Button { Content = "إجراء" };
        var main = new ProjectZ.Views.MainWindow();
        var sidebar = new Button
        {
            Content = new TextBlock { Text = "القائمة الجانبية" },
            Style = (Style)main.Resources["SidebarMenuButtonStyle"]
        };
        host.Children.Add(plain);
        host.Children.Add(body);
        host.Children.Add(heading);
        host.Children.Add(button);
        host.Children.Add(sidebar);
        PumpDispatcher();
        probe.Measure(new Size(520, 360));
        probe.Arrange(new Rect(0, 0, 520, 360));
        probe.UpdateLayout();
        PumpDispatcher();

        Color Effective(TextBlock text) => ((SolidColorBrush)text.Foreground).Color;
        Color Resource(string role) => (Color)Application.Current.Resources["Theme.Color." + role];

        runtime.Preview(new AppearanceSettings { ThemeId = "glassmorphism" });
        PumpDispatcher();
        var plainGlass = Effective(plain);
        var bodyGlass = Effective(body);
        var headingGlass = Effective(heading);
        var buttonGlass = ((SolidColorBrush)button.Foreground).Color;
        var sidebarGlass = ((SolidColorBrush)sidebar.Foreground).Color;

        runtime.Preview(new AppearanceSettings { ThemeId = "dark-glass" });
        PumpDispatcher();
        var plainDark = Effective(plain);
        var bodyDark = Effective(body);
        var headingDark = Effective(heading);
        var buttonDark = ((SolidColorBrush)button.Foreground).Color;
        var sidebarDark = ((SolidColorBrush)sidebar.Foreground).Color;

        check(plainGlass != plainDark, "Unstyled TextBlock effective Foreground changes between light and dark themes");
        check(plainDark == Resource("Text"), "Unstyled TextBlock inherits the live theme Text role");
        check(bodyGlass != bodyDark, "PosRtlTextStyle effective Foreground changes between themes");
        Console.WriteLine($"DEBUG heading glass={headingGlass} want={ResourceAt("glassmorphism", "Heading")} dark={headingDark} want={ResourceAt("dark-glass", "Heading")}");
        check(headingGlass == ResourceAt("glassmorphism", "Heading") && headingDark == ResourceAt("dark-glass", "Heading"),
            "Section headings use the independent Heading role");
        Console.WriteLine($"DEBUG button glass={buttonGlass} want={ResourceAt("glassmorphism", "OnAccent")} dark={buttonDark} want={ResourceAt("dark-glass", "OnAccent")}");
        check(buttonGlass == ResourceAt("glassmorphism", "OnAccent") && buttonDark == ResourceAt("dark-glass", "OnAccent"),
            "Action buttons use the live OnAccent/button-text role");
        Console.WriteLine($"DEBUG sidebar glass={sidebarGlass} want={ResourceAt("glassmorphism", "NavigationInk")} dark={sidebarDark} want={ResourceAt("dark-glass", "NavigationInk")}");
        check(sidebarGlass == ResourceAt("glassmorphism", "NavigationInk") && sidebarDark == ResourceAt("dark-glass", "NavigationInk"),
            "Sidebar menu text uses the independent sidebar-text role");
        check(sidebarGlass != sidebarDark, "Sidebar text visibly differs between presets");

        // Legacy aliases must keep propagating after ThemeManager replaces frozen brushes.
        runtime.Preview(new AppearanceSettings { ThemeId = "glassmorphism" });
        PumpDispatcher();
        var aliasGlass = ((SolidColorBrush)Application.Current.Resources["Theme.Legacy.172B41.Text"]).Color;
        runtime.Preview(new AppearanceSettings { ThemeId = "dark-glass" });
        PumpDispatcher();
        var aliasDark = ((SolidColorBrush)Application.Current.Resources["Theme.Legacy.172B41.Text"]).Color;
        check(aliasGlass != aliasDark && aliasDark == Resource("Text"),
            "Legacy DynamicResource text aliases follow replacement of frozen theme brushes");

        // The same already-open controls must honor independent custom text roles without recreation.
        var custom = new AppearanceSettings
        {
            ThemeId = "custom",
            CustomPalette = ThemeCatalog.CustomPalette with
            {
                Text = "#172B41",
                HeadingText = "#6D28D9",
                SidebarText = "#FFD166",
                SidebarMutedText = "#C6CDD3",
                ButtonText = "#10231F"
            }
        };
        runtime.Preview(custom);
        PumpDispatcher();
        check(Effective(heading) == Resource("Heading") && Effective(heading) != Effective(body),
            "Custom heading text is independent and effective on an already-open heading");
        check(((SolidColorBrush)sidebar.Foreground).Color == Resource("NavigationInk")
            && ((SolidColorBrush)sidebar.Foreground).Color != Resource("Text"),
            "Custom sidebar text is independent and effective on an already-open menu button");
        check(((SolidColorBrush)button.Foreground).Color == Resource("OnAccent")
            && ((SolidColorBrush)button.Foreground).Color != Resource("NavigationInk"),
            "Custom button text is independent and effective on an already-open action button");
    }

    private static Color ResourceAt(string themeId, string role)
    {
        var settings = new AppearanceSettings { ThemeId = themeId };
        var p = ThemeCatalog.PaletteFor(settings);
        var hex = role switch
        {
            "Heading" => ThemeColorMath.EnsureTextContrast(p.HeadingText!, p.Surface),
            "NavigationInk" => ThemeColorMath.EnsureTextContrast(p.SidebarText!, p.Sidebar),
            "OnAccent" => ThemeColorMath.EnsureTextContrast(p.ButtonText!, p.Accent),
            _ => p.Text
        };
        var (r, g, b) = ThemeColorMath.Parse(hex);
        return Color.FromRgb(r, g, b);
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
