using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class InvoicePrintPreviewWindow : Window
{
    public InvoicePrintPreviewWindow(InvoicePrintPreviewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose = Close;
    }
}
