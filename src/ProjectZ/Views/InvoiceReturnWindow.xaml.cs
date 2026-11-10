using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class InvoiceReturnWindow : Window
{
    public InvoiceReturnWindow(InvoiceReturnViewModel viewModel)
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
