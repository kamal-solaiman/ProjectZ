using System.Windows;
using System.Windows.Controls;
using FitohPhone.ViewModels;
using FitohPhone.Views.Dialogs;

namespace FitohPhone.Views.Pages;

public partial class PhoneRepairsView : UserControl
{
    public PhoneRepairsView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        DataContext = new PhoneRepairsViewModel(app.PhoneRepairRepo, App.SessionUserId);
    }

    private void OpenAddCustomer_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddCustomerWindow { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }

    private void OpenBarcodePreview_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new RepairBarcodeWindow { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }
}
