using FitohPhone.Helpers;
using FitohPhone.Models;
namespace FitohPhone.ViewModels;
public sealed class MainViewModel(UserSession user, Action logout)
{
    public string Welcome => $"مرحبًا، {user.DisplayName}";
    public string Username => user.Username;
    public RelayCommand LogoutCommand { get; } = new(logout);
}
