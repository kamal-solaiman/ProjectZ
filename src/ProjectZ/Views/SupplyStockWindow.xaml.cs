using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class SupplyStockWindow : Window
{
    public SupplyStockWindow(SupplyStockViewModel viewModel)
    {
        InitializeComponent();
        viewModel.RequestCloseWithResult = result =>
        {
            DialogResult = result;
            Close();
        };
        DataContext = viewModel;
        Loaded += (_, _) =>
        {
            QuantityInput.Focus();
            QuantityInput.SelectAll();
        };
        Closed += (_, _) =>
        {
            viewModel.RequestCloseWithResult = null;
            DataContext = null;
        };
    }
}
