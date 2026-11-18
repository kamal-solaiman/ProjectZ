using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class SupplierCreditWindow : Window
{
    public SupplierCreditWindow(SupplierCreditDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestCloseWithSuccess = () => DialogResult = true;
    }
}
