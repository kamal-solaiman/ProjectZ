using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class EditPurchaseInvoiceWindow : Window
{
    public EditPurchaseInvoiceWindow(EditPurchaseInvoiceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestCloseWithSuccess = () => DialogResult = true;
        viewModel.RequestCloseCancel = () => DialogResult = false;
    }
}
