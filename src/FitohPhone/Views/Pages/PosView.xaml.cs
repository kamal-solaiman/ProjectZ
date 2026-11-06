using System.Windows;
using System.Windows.Controls;
using FitohPhone.ViewModels;
using FitohPhone.Views.Dialogs;

namespace FitohPhone.Views.Pages;

public partial class PosView : UserControl
{
    public PosView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        DataContext = new PosViewModel(app.ProductRepo, app.CustomerRepo, app.SalesInvoiceRepo, App.SessionUserId);
    }

    private void OpenAddCustomer_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddCustomerWindow { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }
}
