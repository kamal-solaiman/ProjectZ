using System.Windows;
using System.Windows.Controls;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class InvoicesView : UserControl
{
    public InvoicesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is InvoicesViewModel oldVm)
        {
            oldVm.OpenPrintPreviewRequested = null;
            oldVm.OpenReturnDialogRequested = null;
            oldVm.OpenCollectPaymentRequested = null;
            oldVm.OpenConfirmDialogRequested = null;
        }

        if (e.NewValue is InvoicesViewModel newVm)
        {
            newVm.OpenPrintPreviewRequested = vm => ShowOwnedDialog(new InvoicePrintPreviewWindow(vm));
            newVm.OpenReturnDialogRequested = vm => ShowOwnedDialog(new InvoiceReturnWindow(vm));
            newVm.OpenCollectPaymentRequested = vm => ShowOwnedDialog(new CollectInvoicePaymentWindow(vm));
            newVm.OpenConfirmDialogRequested = (vm, onConfirmed) =>
            {
                if (ShowOwnedDialog(new ConfirmActionWindow(vm)) == true)
                    onConfirmed();
            };
        }
    }

    private bool? ShowOwnedDialog(Window dialog)
    {
        if (Window.GetWindow(this) is { } owner)
            dialog.Owner = owner;

        return dialog.ShowDialog();
    }
}
