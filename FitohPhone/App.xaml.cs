using System.Windows;
using FitohPhone.Models;
using FitohPhone.Views;

namespace FitohPhone;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        while (true)
        {
            var loginWindow = new LoginWindow();
            bool? loginResult = loginWindow.ShowDialog();

            if (loginResult != true || loginWindow.LoggedInUser is null)
            {
                Shutdown();
                return;
            }

            var mainWindow = new MainWindow(loginWindow.LoggedInUser);
            mainWindow.ShowDialog();
            // After logout from MainWindow the loop returns to the Login window.
        }
    }
}
