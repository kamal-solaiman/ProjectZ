using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class MaintenanceReceiptPreviewWindow : Window
{
    public MaintenanceReceiptPreviewWindow(MaintenanceReceiptPreviewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose = Close;
    }
}
