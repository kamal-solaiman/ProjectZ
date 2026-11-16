using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class ExpenseInvoiceDetailsWindow : Window
{
    public ExpenseInvoiceDetailsWindow(ExpenseInvoiceDetailsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose = Close;
    }
}
