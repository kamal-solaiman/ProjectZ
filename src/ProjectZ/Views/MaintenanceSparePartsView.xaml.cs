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
            oldVm.OpenConfirmDialogRequested = null;

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
        }
    }
}
