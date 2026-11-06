using System.Windows;
using System.Windows.Controls;
using FitohPhone.ViewModels;

namespace FitohPhone.Views.Pages;

public partial class PosView : UserControl
{
    private readonly PosViewModel _vm;

    public PosView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        _vm = new PosViewModel(app.ProductRepo, app.CustomerRepo, app.SalesInvoiceRepo, App.SessionUserId);
        DataContext = _vm;

        // عند تحميل الصفحة، جلب المنتجات والعملاء من قاعدة البيانات
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
            // إعادة تحميل العملاء بعد إضافة عميل جديد
            _ = _vm.LoadCommand.ExecuteAsync(null);
        }
    }

    private void AllCash_Click(object sender, RoutedEventArgs e)
    {
        _vm.CashAmount = _vm.InvoiceTotal;
        _vm.VodafoneAmount = 0;
        _vm.InstaPayAmount = 0;
    }

    private void AllVodafone_Click(object sender, RoutedEventArgs e)
    {
        _vm.CashAmount = 0;
        _vm.VodafoneAmount = _vm.InvoiceTotal;
        _vm.InstaPayAmount = 0;
    }

    private void AllInstaPay_Click(object sender, RoutedEventArgs e)
    {
        _vm.CashAmount = 0;
        _vm.VodafoneAmount = 0;
        _vm.InstaPayAmount = _vm.InvoiceTotal;
    }
}
