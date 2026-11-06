using System.Windows.Controls;
using FitohPhone.ViewModels;

namespace FitohPhone.Views.Pages;

public partial class UsersView : UserControl
{
    public UsersView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        DataContext = new UsersViewModel(app.UserRepo, app.UserRoleRepo, App.SessionUserId);
    }
}
