using System.Windows;
using System.Windows.Controls;
using FitohPhone.ViewModels;

namespace FitohPhone.Views.Pages;

public partial class CustomersSuppliersView : UserControl
{
    private readonly CustomersSuppliersViewModel _vm;

    public CustomersSuppliersView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        _vm = new CustomersSuppliersViewModel(app.CustomerRepo, app.SupplierRepo, App.SessionUserId);
        DataContext = _vm;

        Loaded += async (_, _) =>
        {
            if (_vm.LoadCommand.CanExecute(null))
                await _vm.LoadCommand.ExecuteAsync(null);
        };
    }

    private void OpenAddCustomer_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Views.Dialogs.AddCustomerWindow { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            // إعادة تحميل القوائم بعد إضافة عميل جديد
            _ = _vm.LoadCommand.ExecuteAsync(null);
        }
    }

    private void OpenAddSupplier_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Views.Dialogs.AddSupplierWindow { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            _ = _vm.LoadCommand.ExecuteAsync(null);
        }
    }

    private async void EditCustomer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int customerId })
        {
            var customer = _vm.Customers.FirstOrDefault(c => c.Id == customerId);
            if (customer != null)
            {
                // فتح نافذة تعديل العميل (يمكن إضافة نافذة تعديل مخصصة لاحقًا)
                MessageBox.Show($"تعديل بيانات العميل: {customer.Name}", "تعديل عميل", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private async void CollectCustomer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int customerId })
        {
            var customer = _vm.Customers.FirstOrDefault(c => c.Id == customerId);
            if (customer != null)
            {
                MessageBox.Show($"تحصيل المبلغ المستحق من العميل: {customer.Name}\nالمبلغ: {customer.OutstandingBalance:N0} ج.م", "تحصيل عميل", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private async void DeleteCustomer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int customerId })
        {
            var customer = _vm.Customers.FirstOrDefault(c => c.Id == customerId);
            if (customer != null &&
                MessageBox.Show($"هل أنت متأكد من حذف العميل: {customer.Name}؟", "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                var ok = await _vm.DeleteCustomerAsync(customerId, System.Threading.CancellationToken.None);
                if (ok)
                {
                    _ = _vm.LoadCommand.ExecuteAsync(null);
                }
            }
        }
    }

    private async void EditSupplier_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int supplierId })
        {
            var supplier = _vm.Suppliers.FirstOrDefault(s => s.Id == supplierId);
            if (supplier != null)
            {
                MessageBox.Show($"تعديل بيانات المورد: {supplier.Name}", "تعديل مورد", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private async void PaySupplier_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int supplierId })
        {
            var supplier = _vm.Suppliers.FirstOrDefault(s => s.Id == supplierId);
            if (supplier != null)
            {
                MessageBox.Show($"سداد دفعة للمورد: {supplier.Name}\nالمستحق عليه: {supplier.OutstandingBalance:N0} ج.م", "سداد مورد", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private async void DeleteSupplier_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int supplierId })
        {
            var supplier = _vm.Suppliers.FirstOrDefault(s => s.Id == supplierId);
            if (supplier != null &&
                MessageBox.Show($"هل أنت متأكد من حذف المورد: {supplier.Name}؟", "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                var ok = await _vm.DeleteSupplierAsync(supplierId, System.Threading.CancellationToken.None);
                if (ok)
                {
                    _ = _vm.LoadCommand.ExecuteAsync(null);
                }
            }
        }
    }

    private async void CustomerRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Border { DataContext: Customer customer })
        {
            await _vm.LoadCustomerDetailsAsync(customer.Name);
        }
    }

    private async void SupplierRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Border { DataContext: Supplier supplier })
        {
            // يمكن عرض تفاصيل المورد في اللوحة اليمنى مستقبلاً
            _vm.SelectedSupplierName = supplier.Name;
            _vm.SelectedSupplierPhone = supplier.Phone;
        }
    }
}
