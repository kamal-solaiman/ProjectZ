using System.Windows;
using ProjectZ.Models;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class AddSupplierWindow : Window
{
    public SupplierDesignItem? CreatedSupplier { get; private set; }

    public AddSupplierWindow(
        string? initialQuery = null,
        Func<string, bool>? isDuplicateSupplier = null,
        SupplierDesignItem? existingSupplier = null,
        Func<string, int?, string?>? customValidator = null,
        string? customHeaderSubtitle = null,
        string? customBannerHint = null,
        string? customSaveButtonText = null)
    {
        InitializeComponent();

        var vm = new AddSupplierViewModel(
            initialQuery,
            existingSupplier,
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
            },
            customValidator,
            customHeaderSubtitle,
            customBannerHint,
            customSaveButtonText);
        DataContext = vm;
        Title = vm.WindowTitle;

        Loaded += (_, _) => SupplierNameInput.Focus();
        Closed += (_, _) => DataContext = null;
    }
}
