using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class PurchaseReturnWindow : Window
{
    public PurchaseReturnWindow(PurchaseReturnViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestCloseWithSuccess = () => DialogResult = true;
        viewModel.RequestCloseCancel = () => DialogResult = false;
    }
}
