using System.Windows;
using System.Windows.Controls;
using FitohPhone.Helpers;
using FitohPhone.ViewModels;

namespace FitohPhone.Views.Pages;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        DataContext = new SettingsViewModel(app.ShopSettingsRepo, App.SessionUserId);
    }

    private void ApplyThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string themeKey })
        {
            ThemeManager.ApplyTheme(themeKey);
        }
    }
}
