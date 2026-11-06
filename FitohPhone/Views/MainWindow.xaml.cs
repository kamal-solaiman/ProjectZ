using System.Windows;
using FitohPhone.Models;
using FitohPhone.ViewModels;

namespace FitohPhone.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(User user)
    {
        InitializeComponent();
        _viewModel = new MainViewModel(user);
        _viewModel.LogoutRequested += (_, _) => Close();  // returns to LoginWindow in App loop
        DataContext = _viewModel;
    }
}
