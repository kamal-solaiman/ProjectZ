using System.Windows;
using System.Windows.Controls;
using FitohPhone.Views.Dialogs;

namespace FitohPhone.Views.Pages;

public partial class CustomersSuppliersView : UserControl
{
    public CustomersSuppliersView() => InitializeComponent();

    private void OpenAddCustomer_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddCustomerWindow { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }

    private void OpenAddSupplier_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddSupplierWindow { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }
}
