using System.Windows.Controls;
using FitohPhone.ViewModels;

namespace FitohPhone.Views.Pages;

public partial class InvoicesLogView : UserControl
{
    public InvoicesLogView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        DataContext = new InvoicesLogViewModel(app.SalesInvoiceRepo, App.SessionUserId);
    }
}
