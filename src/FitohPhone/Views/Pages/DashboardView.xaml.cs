using System.Windows;
using System.Windows.Controls;
using FitohPhone.ViewModels;

namespace FitohPhone.Views.Pages;

public partial class DashboardView : UserControl
{
    private readonly DashboardViewModel _vm;

    public DashboardView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        _vm = new DashboardViewModel(app.DashboardRepo, app.SettingsProvider, App.SessionUserId);
        DataContext = _vm;

        // عند تحميل الصفحة، جلب البيانات الفعلية من قاعدة البيانات
        Loaded += async (_, _) =>
        {
            if (_vm.LoadCommand.CanExecute(null))
                await _vm.LoadCommand.ExecuteAsync(null);
        };
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.LoadCommand.CanExecute(null))
            await _vm.LoadCommand.ExecuteAsync(null);
    }
}
