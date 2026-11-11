using System.Windows;
using ProjectZ.Models;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class AddCustomerWindow : Window
{
    public PosCustomerDesignItem? CreatedCustomer { get; private set; }

    public AddCustomerWindow(string? initialQuery = null)
        : this(initialQuery, null, null, null, null, null)
    {
    }

    public AddCustomerWindow(
        string? initialQuery,
        PosCustomerDesignItem? existingCustomer,
        Func<string, int?, string?>? customValidator,
        string? customHeaderSubtitle,
        string? customBannerHint,
        string? customSaveButtonText)
    {
        InitializeComponent();

        DataContext = new AddCustomerViewModel(
            initialQuery,
            existingCustomer,
            customer =>
            {
                CreatedCustomer = customer;
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

        Loaded += (_, _) =>
        {
            CustomerNameInput.Focus();
            CustomerNameInput.SelectAll();
        };
        Closed += (_, _) => DataContext = null;
    }
}
