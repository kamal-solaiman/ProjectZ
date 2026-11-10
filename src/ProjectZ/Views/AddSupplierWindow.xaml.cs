using System.Windows;
using ProjectZ.Models;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class AddSupplierWindow : Window
{
    public SupplierDesignItem? CreatedSupplier { get; private set; }

    public AddSupplierWindow(string? initialQuery = null, Func<string, bool>? isDuplicateSupplier = null)
    {
        InitializeComponent();

        DataContext = new AddSupplierViewModel(
            initialQuery,
            isDuplicateSupplier,
            supplier =>
            {
                CreatedSupplier = supplier;
                DialogResult = true;
                Close();
            },
            () =>
            {
                DialogResult = false;
                Close();
            });

        Loaded += (_, _) => SupplierNameInput.Focus();
        Closed += (_, _) => DataContext = null;
    }
}
