using System.Windows;
using FitohPhone.Models;
using FitohPhone.ViewModels;

namespace FitohPhone.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public User? LoggedInUser { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        _viewModel = new LoginViewModel();
        _viewModel.LoginSucceeded += OnLoginSucceeded;
        _viewModel.PasswordProvider = () => PasswordBox.Password;
        DataContext = _viewModel;
        Loaded += (_, _) => UsernameTextBox.Focus();
    }

    private void OnLoginSucceeded(object? sender, User user)
    {
        LoggedInUser = user;
        DialogResult = true;   // closes the dialog; App opens MainWindow
        Close();
    }
}
