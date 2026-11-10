using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class MaintenanceDeliveryWindow : Window
{
    public MaintenanceDeliveryWindow(MaintenanceDeliveryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose = () =>
        {
            DialogResult = viewModel.IsConfirmed;
            Close();
        };
    }
}
