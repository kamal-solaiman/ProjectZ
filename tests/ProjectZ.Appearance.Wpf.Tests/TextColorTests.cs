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
        check(headingGlass == Resource("Heading") && headingDark == Resource("Heading"),
            "Section headings use the independent Heading role");
        check(buttonGlass == Resource("OnAccent") && buttonDark == Resource("OnAccent"),
            "Action buttons use the live OnAccent/button-text role");
        check(sidebarGlass == Resource("NavigationInk") && sidebarDark == Resource("NavigationInk"),
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
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
