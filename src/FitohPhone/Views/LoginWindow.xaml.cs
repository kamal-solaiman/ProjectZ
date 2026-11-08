using System.Windows;
namespace FitohPhone.Views;
public partial class LoginWindow : Window
{
    private readonly CancellationTokenSource lifetime = new();
    public CancellationToken LifetimeToken => lifetime.Token;
    public LoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => UsernameInput.Focus();
        Closed += (_, _) => { lifetime.Cancel(); ClearPassword(); DataContext = null; lifetime.Dispose(); };
    }
    public string ReadPassword() => PasswordInput.Password;
    public void ClearPassword() => PasswordInput.Clear();
}
