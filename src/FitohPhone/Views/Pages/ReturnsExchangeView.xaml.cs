using System.Windows.Controls;
using FitohPhone.ViewModels;

namespace FitohPhone.Views.Pages;

public partial class ReturnsExchangeView : UserControl
{
    public ReturnsExchangeView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        DataContext = new ReturnsExchangeViewModel(app.CustomerRepo, app.ProductRepo, app.SalesInvoiceRepo, app.ReturnExchangeRepo, App.SessionUserId);
    }
}
