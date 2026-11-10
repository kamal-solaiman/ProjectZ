using System.Windows;
using System.Windows.Controls;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class PurchaseInvoicesView : UserControl
{
    public PurchaseInvoicesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PurchaseInvoicesViewModel oldVm)
        {
            oldVm.OpenDetailsDialogRequested = null;
            oldVm.OpenPrintPreviewRequested = null;
            oldVm.OpenPaySupplierDialogRequested = null;
            oldVm.OpenEditDialogRequested = null;
            oldVm.OpenReturnDialogRequested = null;
            oldVm.OpenConfirmDialogRequested = null;
        }

        if (e.NewValue is PurchaseInvoicesViewModel newVm)
        {
            newVm.OpenDetailsDialogRequested = vm => ShowOwnedDialog(new PurchaseInvoiceDetailsWindow(vm));
            newVm.OpenPrintPreviewRequested = vm => ShowOwnedDialog(new PurchaseInvoicePrintPreviewWindow(vm));
            newVm.OpenPaySupplierDialogRequested = vm => ShowOwnedDialog(new PaySupplierInvoiceWindow(vm));
            newVm.OpenEditDialogRequested = vm => ShowOwnedDialog(new EditPurchaseInvoiceWindow(vm));
            newVm.OpenReturnDialogRequested = vm => ShowOwnedDialog(new PurchaseReturnWindow(vm));
            newVm.OpenConfirmDialogRequested = (vm, onConfirmed) =>
            {
                if (ShowOwnedDialog(new ConfirmActionWindow(vm)) == true)
                    onConfirmed();
            };
        }
    }

    private bool? ShowOwnedDialog(Window dialog)
    {
        var owner = ResolveDialogOwner();
        if (owner is not null && !ReferenceEquals(owner, dialog))
            dialog.Owner = owner;

        return dialog.ShowDialog();
    }

    private Window? ResolveDialogOwner()
    {
        if (Application.Current is not null)
        {
            for (var i = Application.Current.Windows.Count - 1; i >= 0; i--)
            {
                var candidate = Application.Current.Windows[i];
                if (candidate.IsVisible && candidate.IsActive)
                    return candidate;
            }

            for (var i = Application.Current.Windows.Count - 1; i >= 0; i--)
            {
                var candidate = Application.Current.Windows[i];
                if (candidate.IsVisible)
                    return candidate;
            }
        }

        return Window.GetWindow(this);
    }
}
