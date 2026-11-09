using System.Windows;
using System.Windows.Controls;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class InventoryView : UserControl
{
    public InventoryView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is InventoryViewModel oldVm)
        {
            oldVm.OpenRegistrationsDialogRequested = null;
            oldVm.OpenSupplyDialogRequested = null;
            oldVm.OpenEditDialogRequested = null;
            oldVm.OpenBarcodePreviewRequested = null;
            oldVm.OpenMarkDamagedDialogRequested = null;
            oldVm.OpenConfirmDialogRequested = null;
        }

        if (e.NewValue is InventoryViewModel newVm)
        {
            newVm.OpenRegistrationsDialogRequested = vm => ShowOwnedDialog(new ProductRegistrationsWindow(vm));
            newVm.OpenSupplyDialogRequested = vm => ShowOwnedDialog(new SupplyStockWindow(vm));
            newVm.OpenEditDialogRequested = vm => ShowOwnedDialog(new EditProductWindow(vm));
            newVm.OpenBarcodePreviewRequested = vm => ShowOwnedDialog(new BarcodePrintPreviewWindow(vm));
            newVm.OpenMarkDamagedDialogRequested = vm => ShowOwnedDialog(new MarkDamagedWindow(vm));
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
