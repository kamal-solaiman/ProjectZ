using System.Windows;
namespace FitohPhone.Views;
public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => UsernameInput.Focus();
        // The sign-in CancellationTokenSource is owned by App (see App.xaml.cs):
        // App cancels it on close and disposes it in OnExit, after any pending
        // SqlConnection.OpenAsync registration has finished.
        Closed += (_, _) => { ClearPassword(); DataContext = null; };
    }
    public string ReadPassword() => PasswordInput.Password;
    public void ClearPassword() => PasswordInput.Clear();
}
