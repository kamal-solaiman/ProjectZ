using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class PurchaseInvoiceDetailsWindow : Window
{
    public PurchaseInvoiceDetailsWindow(PurchaseInvoiceDetailsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose = Close;
    }
}
