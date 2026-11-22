using System.Windows;
using System.Windows.Controls;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class AddProductView : UserControl
{
    public AddProductView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => FocusProductNameInput();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) return;

            // The categories screen shares this list, so re-point the combo box at the current rows.
            if (DataContext is AddProductViewModel viewModel)
                viewModel.RefreshCategoriesFromSharedList();
            FocusProductNameInput();
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is AddProductViewModel oldViewModel)
        {
            oldViewModel.FocusProductNameRequested = null;
            oldViewModel.OpenConfirmDialogRequested = null;
        }

        if (e.NewValue is AddProductViewModel newViewModel)
        {
            newViewModel.FocusProductNameRequested = FocusProductNameInput;
            newViewModel.OpenConfirmDialogRequested = (confirm, onConfirmed) =>
            {
                var dialog = new ConfirmActionWindow(confirm);
                if (Window.GetWindow(this) is { } owner) dialog.Owner = owner;
                if (dialog.ShowDialog() == true) onConfirmed();
            };
        }
    }

    private void FocusProductNameInput()
    {
        if (!IsVisible) return;
        ProductNameInput.Focus();
    }
}
