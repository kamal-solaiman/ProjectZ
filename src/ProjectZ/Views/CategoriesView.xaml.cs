using System.Windows;
using System.Windows.Controls;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class CategoriesView : UserControl
{
    public CategoriesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is CategoriesViewModel oldVm)
            oldVm.OpenConfirmDialogRequested = null;

        if (e.NewValue is CategoriesViewModel newVm)
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

    private void InlineEditBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox box && e.NewValue is true)
        {
            Dispatcher.BeginInvoke(() =>
            {
                box.Focus();
                box.SelectAll();
            });
        }
    }
}
