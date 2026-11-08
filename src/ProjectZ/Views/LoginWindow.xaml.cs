using System.Windows;

namespace ProjectZ.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => UsernameInput.Focus();
        // App owns the sign-in cancellation token. It cancels on close and only
        // disposes the source during application exit, avoiding a race with an
        // in-flight SqlConnection cancellation registration.
        Closed += (_, _) =>
        {
            ClearPassword();
            DataContext = null;
        };
    }

    public string ReadPassword() => PasswordInput.Password;

    public void ClearPassword() => PasswordInput.Clear();
}
