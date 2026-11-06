using System.Windows.Input;
using FitohPhone.Helpers;
using FitohPhone.Models;
using FitohPhone.Services;

namespace FitohPhone.ViewModels;

public sealed class LoginViewModel : ViewModelBase
{
    private readonly AuthService _authService;

    private string _username = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public LoginViewModel() : this(new AuthService()) { }

    public LoginViewModel(AuthService authService)
    {
        _authService = authService;
        LoginCommand = new RelayCommand(_ => Login(PasswordProvider?.Invoke() ?? string.Empty),
                                       _ => !IsBusy);
    }

    /// <summary>
    /// Supplies the current PasswordBox content (PasswordBox.Password is not bindable).
    /// Set by the view: viewModel.PasswordProvider = () => PasswordBox.Password;
    /// </summary>
    public Func<string>? PasswordProvider { get; set; }

    /// <summary>Raised when login succeeds. The view uses it to open MainWindow.</summary>
    public event EventHandler<User>? LoginSucceeded;

    public string Username
    {
        get => _username;
        set
        {
            if (SetProperty(ref _username, value))
            {
                ErrorMessage = string.Empty;
                ((RelayCommand)LoginCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                ((RelayCommand)LoginCommand).RaiseCanExecuteChanged();
        }
    }

    public ICommand LoginCommand { get; }

    /// <summary>Called by the view to supply the PasswordBox content (PasswordBox.Password is not bindable).</summary>
    public void Login(string password)
    {
        if (IsBusy)
            return;

        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            LoginResult result = _authService.Login(Username, password);
            if (result.Status == LoginStatus.Success && result.User is not null)
            {
                LoginSucceeded?.Invoke(this, result.User);
            }
            else
            {
                ErrorMessage = result.ErrorMessage;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
