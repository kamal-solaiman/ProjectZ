using System.Windows;
using System.Windows.Controls;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class PurchaseEntryView : UserControl
{
    public PurchaseEntryView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PurchaseEntryViewModel oldVm)
        {
            oldVm.OpenAddSupplierRequested = null;
            oldVm.OpenPrintPreviewRequested = null;
            oldVm.FocusBarcodeInputRequested = null;
        }

        if (e.NewValue is PurchaseEntryViewModel newVm)
        {
            newVm.OpenAddSupplierRequested = (initialQuery, isDuplicate, onCreated) =>
            {
                var window = new AddSupplierWindow(initialQuery, isDuplicate);
                if (Window.GetWindow(this) is { } owner)
                    window.Owner = owner;

                if (window.ShowDialog() == true && window.CreatedSupplier is not null)
                    onCreated(window.CreatedSupplier);
            };

            newVm.OpenPrintPreviewRequested = vm =>
            {
                var dialog = new PurchaseInvoicePrintPreviewWindow(vm);
                if (Window.GetWindow(this) is { } owner)
                    dialog.Owner = owner;

                dialog.ShowDialog();
            };

            newVm.FocusBarcodeInputRequested = () => PurchaseBarcodeInput.Focus();
        }
    }
}
