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
            if (IsVisible)
                FocusProductNameInput();
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is AddProductViewModel oldViewModel)
            oldViewModel.FocusProductNameRequested = null;

        if (e.NewValue is AddProductViewModel newViewModel)
            newViewModel.FocusProductNameRequested = FocusProductNameInput;
    }

    private void FocusProductNameInput()
    {
        if (!IsVisible) return;
        ProductNameInput.Focus();
    }
}
