using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class PurchaseInvoicePrintPreviewWindow : Window
{
    public PurchaseInvoicePrintPreviewWindow(PurchaseInvoicePrintPreviewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose = Close;
        Closed += (_, _) =>
        {
            viewModel.RequestClose = null;
            DataContext = null;
        };
    }
}
