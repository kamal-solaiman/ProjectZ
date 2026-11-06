using System.Windows;
using System.Windows.Controls;
using FitohPhone.ViewModels;

namespace FitohPhone.Views.Pages;

public partial class AddProductView : UserControl
{
    private readonly AddProductViewModel _vm;

    public AddProductView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        _vm = new AddProductViewModel(app.ProductRepo, app.CategoryRepo, app.BrandRepo, App.SessionUserId);
        DataContext = _vm;

        // عند تحميل الصفحة، جلب التصنيفات والماركات والمنتجات من قاعدة البيانات
        Loaded += async (_, _) =>
        {
            if (_vm.LoadListsCommand.CanExecute(null))
                await _vm.LoadListsCommand.ExecuteAsync(null);
        };
    }

    private void GenerateBarcode_Click(object sender, RoutedEventArgs e)
    {
        // توليد باركود عشوائي بسيط للمוצר الجديد
        var rnd = new System.Random();
        var barcode = rnd.Next(100000000, 999999999).ToString();
        // يمكن تعيين الباركود المGenerated إلى حقل الباركود إذا كان فارغًا
        // لكن الحقل مربوط بـ Binding، لذا لا يمكن التعيين مباشرة من هنا بدون الوصول إلى ViewModel
        MessageBox.Show($"الباركود المGenerated: {barcode}\nيمكن نسخها ولصقها في حقل الباركود.", "توليد باركود", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SaveProduct_Click(object sender, RoutedEventArgs e)
    {
        var ok = _vm.SaveCommand.CanExecute(null);
        if (ok)
        {
            var saved = _vm.SaveCommand.ExecuteAsync(null).Result;
            if (saved)
            {
                MessageBox.Show("تم حفظ وتسجيل الصنف الجديد بنجاح في قاعدة البيانات.", "حفظ الصنف", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(_vm.Error, "فشل الحفظ", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void SaveAndPrintBarcode_Click(object sender, RoutedEventArgs e)
    {
        var ok = _vm.SaveCommand.CanExecute(null);
        if (ok)
        {
            var saved = _vm.SaveCommand.ExecuteAsync(null).Result;
            if (saved)
            {
                MessageBox.Show("تم حفظ الصنف وطباعة باركود المنتج بنجاح.", "حفظ وطباعة", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(_vm.Error, "فشل الحفظ", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void ClearFields_Click(object sender, RoutedEventArgs e)
    {
        _vm.Barcode = "";
        _vm.NameAr = "";
        _vm.NameEn = null;
        _vm.CategoryId = null;
        _vm.BrandId = null;
        _vm.CostPrice = 0;
        _vm.PriceWholesale = 0;
        _vm.PriceSpecial = 0;
        _vm.PriceRetail = 0;
        _vm.MinStock = 0;
        _vm.CurrentStock = 0;
        _vm.Unit = "قطعة";
        _vm.WarrantyMonths = null;
        _vm.Error = "";
    }
}
