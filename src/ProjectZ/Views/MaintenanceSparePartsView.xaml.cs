using System.Windows;
using System.Windows.Controls;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class MaintenanceSparePartsView : UserControl
{
    public MaintenanceSparePartsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MaintenanceSparePartsViewModel oldVm)
        {
            oldVm.OpenConfirmDialogRequested = null;
            oldVm.OpenRestockDialogRequested = null;
            // The supplier/customer navigation callbacks belong to MainViewModel; do not clear
            // them here, otherwise reusing the same view model would silently break the links.
        }

        if (e.NewValue is MaintenanceSparePartsViewModel newVm)
        {
            newVm.OpenConfirmDialogRequested = (vm, onConfirmed) =>
            {
                var dialog = new ConfirmActionWindow(vm);
                if (Window.GetWindow(this) is { } owner)
                    dialog.Owner = owner;

                if (dialog.ShowDialog() == true)
                    onConfirmed();
            };

            newVm.OpenRestockDialogRequested = vm =>
            {
                var dialog = new MaintenanceSparePartRestockWindow(vm);
                if (Window.GetWindow(this) is { } owner)
                    dialog.Owner = owner;

                return dialog.ShowDialog();
            };
        }
    }
}
