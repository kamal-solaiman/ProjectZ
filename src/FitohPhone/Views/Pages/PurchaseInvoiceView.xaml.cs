using System.Windows;
using System.Windows.Controls;
using FitohPhone.ViewModels;
using FitohPhone.Views.Dialogs;

namespace FitohPhone.Views.Pages;

public partial class PurchaseInvoiceView : UserControl
{
    public PurchaseInvoiceView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        DataContext = new PurchaseInvoiceViewModel(app.SupplierRepo, app.ProductRepo, app.PurchaseInvoiceRepo, App.SessionUserId);
    }

    private void OpenAddSupplier_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddSupplierWindow { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }
}
