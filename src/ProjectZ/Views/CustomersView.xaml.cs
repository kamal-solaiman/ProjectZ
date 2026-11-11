using System.Windows;
using System.Windows.Controls;
using ProjectZ.Models;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class CustomersView : UserControl
{
    public CustomersView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is CustomersViewModel oldVm)
        {
            oldVm.OpenAddCustomerDialogRequested = null;
            oldVm.OpenEditCustomerDialogRequested = null;
            oldVm.OpenConfirmDialogRequested = null;
            oldVm.OpenCollectSalesPaymentRequested = null;
            oldVm.OpenSalesPrintPreviewRequested = null;
            oldVm.OpenMaintenanceDeliveryRequested = null;
            oldVm.OpenMaintenanceDetailsRequested = null;
            oldVm.OpenMaintenanceReceiptPreviewRequested = null;
        }

        if (e.NewValue is CustomersViewModel newVm)
        {
            newVm.OpenAddCustomerDialogRequested = (initialQuery, validator, onSaved) =>
            {
                var dialog = new AddCustomerWindow(
                    initialQuery,
                    null,
                    validator,
                    "إضافة عميل جديد إلى الدليل الموحد (المبيعات والصيانة)",
                    "يتم تسجيل العميل في الدليل العام ليظهر فورًا في نقطة البيع واستلام أجهزة الصيانة.",
                    "حفظ العميل");
                if (ShowOwnedDialog(dialog) == true && dialog.CreatedCustomer is { } created)
                    onSaved(created);
            };

            newVm.OpenEditCustomerDialogRequested = (customer, validator, onSaved) =>
            {
                var dialog = new AddCustomerWindow(
                    null,
                    customer,
                    validator,
                    null,
                    null,
                    "حفظ التعديلات");
                if (ShowOwnedDialog(dialog) == true && dialog.CreatedCustomer is { } updated)
                    onSaved(updated);
            };

            newVm.OpenConfirmDialogRequested = (vm, onConfirmed) =>
            {
                if (ShowOwnedDialog(new ConfirmActionWindow(vm)) == true)
                    onConfirmed();
            };

            newVm.OpenCollectSalesPaymentRequested = vm => ShowOwnedDialog(new CollectInvoicePaymentWindow(vm));
            newVm.OpenSalesPrintPreviewRequested = vm => ShowOwnedDialog(new InvoicePrintPreviewWindow(vm));
            newVm.OpenMaintenanceDeliveryRequested = vm => ShowOwnedDialog(new MaintenanceDeliveryWindow(vm));
            newVm.OpenMaintenanceDetailsRequested = vm => ShowOwnedDialog(new MaintenanceInvoiceDetailsWindow(vm));
            newVm.OpenMaintenanceReceiptPreviewRequested = vm => ShowOwnedDialog(new MaintenanceReceiptPreviewWindow(vm));
        }
    }

    private bool? ShowOwnedDialog(Window dialog)
    {
        if (Window.GetWindow(this) is { } owner)
            dialog.Owner = owner;

        return dialog.ShowDialog();
    }
}
