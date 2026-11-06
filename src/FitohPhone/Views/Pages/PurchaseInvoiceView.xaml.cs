using System.Windows;
using System.Windows.Controls;
using FitohPhone.ViewModels;

namespace FitohPhone.Views.Pages;

public partial class PurchaseInvoiceView : UserControl
{
    private readonly PurchaseInvoiceViewModel _vm;

    public PurchaseInvoiceView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        _vm = new PurchaseInvoiceViewModel(app.SupplierRepo, app.ProductRepo, app.PurchaseInvoiceRepo, App.SessionUserId);
        DataContext = _vm;

        // عند تحميل الصفحة، جلب الموردين والمنتجات من قاعدة البيانات
        Loaded += async (_, _) =>
        {
            if (_vm.LoadCommand.CanExecute(null))
                await _vm.LoadCommand.ExecuteAsync(null);
        };
    }

    private void OpenAddSupplier_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Views.Dialogs.AddSupplierWindow { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            // إعادة تحميل الموردين بعد إضافة تاجر جديد
            _ = _vm.LoadCommand.ExecuteAsync(null);
        }
    }

    private void AddProduct_Click(object sender, RoutedEventArgs e)
    {
        if (ProductSearch.SelectedItem is Product selectedProduct &&
            int.TryParse(PurchaseQty.Text, out var qty) &&
            decimal.TryParse(PurchaseCost.Text, out var cost))
        {
            _vm.AddProductToLine(selectedProduct, qty, cost);
            PurchaseQty.Text = "1";
            PurchaseCost.Text = "0";
        }
    }
}
