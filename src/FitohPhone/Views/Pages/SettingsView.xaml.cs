using System.Windows;
using System.Windows.Controls;
using FitohPhone.ViewModels;

namespace FitohPhone.Views.Pages;

public partial class SettingsView : UserControl
{
    private readonly SettingsViewModel _vm;

    public SettingsView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        _vm = new SettingsViewModel(app.ShopSettingsRepo, App.SessionUserId);
        DataContext = _vm;

        // عند تحميل الصفحة، جلب إعدادات المحل من قاعدة البيانات
        Loaded += async (_, _) =>
        {
            if (_vm.LoadCommand.CanExecute(null))
                await _vm.LoadCommand.ExecuteAsync(null);
        };
    }

    private void ApplyThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string themeKey })
        {
            FitohPhone.Helpers.ThemeManager.ApplyTheme(themeKey);
        }
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SaveCommand.CanExecute(null))
        {
            await _vm.SaveCommand.ExecuteAsync(null);
            if (_vm.SaveSucceeded)
            {
                MessageBox.Show("تم حفظ إعدادات المحل والبرنامج بنجاح.", "حفظ الإعدادات", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                var error = string.IsNullOrWhiteSpace(_vm.Error) ? _vm.Message : _vm.Error;
                MessageBox.Show(error, "فشل الحفظ", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
