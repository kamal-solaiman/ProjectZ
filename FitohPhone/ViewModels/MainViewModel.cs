using System.Windows.Input;
using FitohPhone.Helpers;
using FitohPhone.Models;

namespace FitohPhone.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private string _displayName;

    public MainViewModel(User user)
    {
        _displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;
        LogoutCommand = new RelayCommand(_ => LogoutRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>Raised when the user presses Logout; the view closes MainWindow and returns to Login.</summary>
    public event EventHandler? LogoutRequested;

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public ICommand LogoutCommand { get; }
}
