using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class MaintenanceCancelOrderWindow : Window
{
    public MaintenanceCancelOrderViewModel ViewModel { get; }

    public MaintenanceCancelOrderWindow(MaintenanceCancelOrderViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        viewModel.RequestClose = () =>
        {
            DialogResult = viewModel.IsConfirmed;
            Close();
        };
    }
}
