using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class MaintenanceSparePartRestockWindow : Window
{
    public MaintenanceSparePartRestockViewModel ViewModel { get; }

    public MaintenanceSparePartRestockWindow(MaintenanceSparePartRestockViewModel viewModel)
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
