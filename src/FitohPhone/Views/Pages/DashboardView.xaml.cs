using System.Windows.Controls;
using FitohPhone.ViewModels;

namespace FitohPhone.Views.Pages;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        DataContext = new DashboardViewModel(app.DashboardRepo, app.SettingsProvider, App.SessionUserId);
    }
}
