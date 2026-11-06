using System.Windows;
using System.Windows.Controls;
using FitohPhone.Views.Dialogs;

namespace FitohPhone.Views.Pages;

public partial class PurchaseInvoiceView : UserControl
{
    public PurchaseInvoiceView() => InitializeComponent();

    private void OpenAddSupplier_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddSupplierWindow { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }
}
