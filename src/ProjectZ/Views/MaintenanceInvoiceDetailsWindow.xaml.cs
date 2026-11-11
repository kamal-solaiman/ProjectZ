using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class MaintenanceInvoiceDetailsWindow : Window
{
    public MaintenanceInvoiceDetailsWindow(MaintenanceInvoiceDetailsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RequestClose = Close;
        viewModel.OpenDeliveryDialogRequested = vm => ShowOwnedDialog(new MaintenanceDeliveryWindow(vm));
        viewModel.OpenReceiptPreviewRequested = vm => ShowOwnedDialog(new MaintenanceReceiptPreviewWindow(vm));
    }

    private bool? ShowOwnedDialog(Window dialog)
    {
        dialog.Owner = this;
        return dialog.ShowDialog();
    }
}
