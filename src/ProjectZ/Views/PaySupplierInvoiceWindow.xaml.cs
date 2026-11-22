using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class PaySupplierInvoiceWindow : Window
{
    public PaySupplierInvoiceWindow(PaySupplierInvoiceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestCloseWithSuccess = () => DialogResult = true;
        viewModel.RequestCloseCancel = () => DialogResult = false;
    }
}
