using System.Windows;
using System.Windows.Controls;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class MyAccountsView : UserControl
{
    public MyAccountsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MyAccountsViewModel oldVm)
        {
            oldVm.OpenAccountEditorRequested = null;
            oldVm.OpenTransactionRequested = null;
            oldVm.OpenTransferRequested = null;
            oldVm.OpenConfirmDialogRequested = null;
        }
        if (e.NewValue is not MyAccountsViewModel newVm) return;
        newVm.OpenAccountEditorRequested = vm => ShowOwnedDialog(new AccountEditorWindow(vm));
        newVm.OpenTransactionRequested = vm => ShowOwnedDialog(new AccountTransactionWindow(vm));
        newVm.OpenTransferRequested = vm => ShowOwnedDialog(new AccountTransferWindow(vm));
        newVm.OpenConfirmDialogRequested = (vm, onConfirmed) =>
        {
            if (ShowOwnedDialog(new ConfirmActionWindow(vm)) == true) onConfirmed();
        };
    }

    private bool? ShowOwnedDialog(Window dialog)
    {
        if (Window.GetWindow(this) is { } owner) dialog.Owner = owner;
        return dialog.ShowDialog();
    }
}
