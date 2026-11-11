using System.Windows;
using System.Windows.Controls;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class MaintenanceInvoicesView : UserControl
{
    public MaintenanceInvoicesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MaintenanceInvoicesViewModel oldVm)
        {
            oldVm.OpenDetailsDialogRequested = null;
            oldVm.OpenDeliveryDialogRequested = null;
            oldVm.OpenReceiptPreviewRequested = null;
        }

        if (e.NewValue is MaintenanceInvoicesViewModel newVm)
        {
            newVm.OpenDetailsDialogRequested = vm => ShowOwnedDialog(new MaintenanceInvoiceDetailsWindow(vm));
            newVm.OpenDeliveryDialogRequested = vm => ShowOwnedDialog(new MaintenanceDeliveryWindow(vm));
            newVm.OpenReceiptPreviewRequested = vm => ShowOwnedDialog(new MaintenanceReceiptPreviewWindow(vm));
        }
    }

    private bool? ShowOwnedDialog(Window dialog)
    {
        if (Window.GetWindow(this) is { } owner)
            dialog.Owner = owner;

        return dialog.ShowDialog();
    }
}
