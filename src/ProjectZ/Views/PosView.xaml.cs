using System.Windows;
using System.Windows.Controls;
using ProjectZ.Models;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class PosView : UserControl
{
    public PosView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => FocusBarcodeInput();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                FocusBarcodeInput();
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PosViewModel oldViewModel)
        {
            oldViewModel.OpenAddCustomerRequested = null;
            oldViewModel.FocusBarcodeInputRequested = null;
        }

        if (e.NewValue is PosViewModel newViewModel)
        {
            newViewModel.OpenAddCustomerRequested = OpenAddCustomerDialog;
            newViewModel.FocusBarcodeInputRequested = FocusBarcodeInput;
        }
    }

    private void OpenAddCustomerDialog(string? initialQuery, Action<PosCustomerDesignItem> onCreated)
    {
        var dialog = DataContext is PosViewModel { IsDatabaseBacked: true }
            ? new AddCustomerWindow(
                initialQuery,
                null,
                null,
                "إضافة عميل جديد إلى الدليل الموحد",
                "سيُحفظ العميل في قاعدة البيانات مباشرة ويُختار في الفاتورة الحالية.",
                "حفظ وإدراج في الفاتورة")
            : new AddCustomerWindow(initialQuery);
        if (Window.GetWindow(this) is { } owner)
            dialog.Owner = owner;

        if (dialog.ShowDialog() == true && dialog.CreatedCustomer is { } created)
            onCreated(created);
    }

    private void FocusBarcodeInput()
    {
        if (!IsVisible) return;
        BarcodeSearchInput.Focus();
    }
}
