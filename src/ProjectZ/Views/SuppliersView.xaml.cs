using System.Windows;
using System.Windows.Controls;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class SuppliersView : UserControl
{
    public SuppliersView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SuppliersViewModel oldVm)
        {
            oldVm.OpenAddSupplierDialogRequested = null;
            oldVm.OpenEditSupplierDialogRequested = null;
            oldVm.OpenConfirmDialogRequested = null;
        }

        if (e.NewValue is not SuppliersViewModel newVm) return;

        newVm.OpenAddSupplierDialogRequested = (initialQuery, duplicateName, validator, onSaved) =>
        {
            var dialog = new AddSupplierWindow(
                initialQuery,
                duplicateName,
                customValidator: validator,
                customHeaderSubtitle: "إضافة مورد جديد إلى الدليل الموحد للمشتريات",
                customBannerHint: "سجّل بيانات الاتصال والتخصص والتحويل مرة واحدة؛ تظهر بعدها في فواتير الشراء وملف المورد.",
                customSaveButtonText: "حفظ المورد");
            if (ShowOwnedDialog(dialog) == true && dialog.CreatedSupplier is { } created)
                onSaved(created);
        };

        newVm.OpenEditSupplierDialogRequested = (supplier, validator, onSaved) =>
        {
            var dialog = new AddSupplierWindow(
                existingSupplier: supplier,
                customValidator: validator,
                customSaveButtonText: "حفظ التعديلات");
            if (ShowOwnedDialog(dialog) == true && dialog.CreatedSupplier is { } updated)
                onSaved(updated);
        };

        newVm.OpenConfirmDialogRequested = (vm, onConfirmed) =>
        {
            if (ShowOwnedDialog(new ConfirmActionWindow(vm)) == true)
                onConfirmed();
        };
    }

    private bool? ShowOwnedDialog(Window dialog)
    {
        if (Window.GetWindow(this) is { } owner)
            dialog.Owner = owner;
        return dialog.ShowDialog();
    }
}
