using System.Windows;
using FitohPhone.Data;
using FitohPhone.Models;
using FitohPhone.Services;
using FitohPhone.ViewModels;
using FitohPhone.Views;
namespace FitohPhone;
public partial class App : Application
{
    private UserSession? session;
    private AuthenticationService service = null!;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        service = new AuthenticationService(new UserRepository(new ConnectionSettingsProvider()));
        ShowLogin();
    }
    private void ShowLogin()
    {
        var window = new LoginWindow();
        window.DataContext = new LoginViewModel(service, () => window.ReadPassword(), window.ClearPassword,
            user => OpenMain(window, user), window.LifetimeToken);
        MainWindow = window;
        window.Closed += (_, _) => { if (ReferenceEquals(MainWindow, window)) Shutdown(); };
        window.Show();
    }
    private void OpenMain(LoginWindow login, UserSession user)
    {
        session = user;
        var main = new Views.MainWindow();
        main.DataContext = new MainViewModel(session, () =>
        {
            session = null;
            main.DataContext = null;
            ShowLogin();
            main.Close();
        });
        MainWindow = main;
        main.Closed += (_, _) => { if (ReferenceEquals(MainWindow, main)) { session = null; Shutdown(); } };
        main.Show();
        login.ClearPassword();
        login.Close();
    }
}
