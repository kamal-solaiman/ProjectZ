using System.Windows;
using System.Windows.Controls;
using FitohPhone.Helpers;

namespace FitohPhone.Views.Pages;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private void ApplyThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string themeKey })
        {
            ThemeManager.ApplyTheme(themeKey);
        }
    }
}
