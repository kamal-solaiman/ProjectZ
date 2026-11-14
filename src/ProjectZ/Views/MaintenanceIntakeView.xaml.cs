using System.Windows;
using System.Windows.Controls;
using ProjectZ.Models;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class MaintenanceIntakeView : UserControl
{
    public MaintenanceIntakeView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MaintenanceIntakeViewModel oldVm)
        {
            oldVm.OpenAddCustomerRequested = null;
            oldVm.OpenReceiptPreviewRequested = null;
            oldVm.FocusModelInputRequested = null;
        }

        if (e.NewValue is MaintenanceIntakeViewModel newVm)
        {
            newVm.OpenAddCustomerRequested = OpenAddCustomerDialog;
            newVm.OpenReceiptPreviewRequested = vm => ShowOwnedDialog(new MaintenanceReceiptPreviewWindow(vm));
            newVm.FocusModelInputRequested = () => DeviceModelInput.Focus();
        }
    }

    private void OpenAddCustomerDialog(string? initialQuery, Action<PosCustomerDesignItem> onCreated)
    {
        // With SQL Server behind the screen the dialog must write to dbo.Customers, so it gets the
        // database-aware texts and the duplicate-phone check instead of the design-mode wording.
        var viewModel = DataContext as MaintenanceIntakeViewModel;
        var dialog = viewModel is { IsDatabaseBacked: true }
            ? new AddCustomerWindow(
                initialQuery,
                null,
                viewModel.ValidateNewCustomerPhone,
                "إضافة عميل جديد إلى دليل العملاء",
                "سيُحفظ العميل في قاعدة البيانات مباشرة ويُربط بأمر الصيانة الحالي.",
                "حفظ وإدراج في أمر الصيانة")
            : new AddCustomerWindow(initialQuery);
        if (Window.GetWindow(this) is { } owner)
            dialog.Owner = owner;

        if (dialog.ShowDialog() == true && dialog.CreatedCustomer is { } created)
            onCreated(created);
    }

    private bool? ShowOwnedDialog(Window dialog)
    {
        if (Window.GetWindow(this) is { } owner)
            dialog.Owner = owner;

        return dialog.ShowDialog();
    }
}
