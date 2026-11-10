using System.Windows;
using System.Windows.Controls;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class MaintenanceActionsView : UserControl
{
    public MaintenanceActionsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MaintenanceActionsViewModel oldVm)
        {
            oldVm.OpenDeliveryDialogRequested = null;
            oldVm.OpenCancelDialogRequested = null;
            oldVm.OpenReceiptPreviewRequested = null;
        }

        if (e.NewValue is MaintenanceActionsViewModel newVm)
        {
            newVm.OpenDeliveryDialogRequested = vm => ShowOwnedDialog(new MaintenanceDeliveryWindow(vm));
            newVm.OpenCancelDialogRequested = vm => ShowOwnedDialog(new MaintenanceCancelOrderWindow(vm));
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
