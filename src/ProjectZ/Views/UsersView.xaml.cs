using System.Windows;
using System.Windows.Controls;
using ProjectZ.Data;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class UsersView : UserControl
{
    public UsersView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is UsersViewModel oldVm)
        {
            oldVm.OpenUserEditorRequested = null;
            oldVm.OpenConfirmDialogRequested = null;
        }

        if (e.NewValue is UsersViewModel newVm)
        {
            newVm.OpenUserEditorRequested = (mode, record, onSaved) => OpenEditor(newVm, mode, record, onSaved);
            newVm.OpenConfirmDialogRequested = (vm, onConfirmed) =>
            {
                if (ShowOwnedDialog(new ConfirmActionWindow(vm)) == true)
                    onConfirmed();
            };
        }
    }

    /// <summary>
    /// Builds the add/edit/password dialog. The window's PasswordBox controls are the only holders of the
    /// plain password; the view model reads them at save time and clears them immediately after.
    /// </summary>
    private void OpenEditor(UsersViewModel owner, string mode, UserRecord? record, Action<UserRecord> onSaved)
    {
        if (owner.Repository is not { } repository) return;

        var dialog = new UserEditorWindow(mode, repository, record, onSaved);
        ShowOwnedDialog(dialog);
    }

    private bool? ShowOwnedDialog(Window dialog)
    {
        if (Window.GetWindow(this) is { } window)
            dialog.Owner = window;

        return dialog.ShowDialog();
    }
}
