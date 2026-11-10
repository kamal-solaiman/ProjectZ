using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class CollectInvoicePaymentWindow : Window
{
    public CollectInvoicePaymentWindow(CollectInvoicePaymentViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestCloseWithResult = result =>
        {
            DialogResult = result;
            Close();
        };
    }
}
