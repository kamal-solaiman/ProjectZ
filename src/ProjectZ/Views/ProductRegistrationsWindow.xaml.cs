using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class ProductRegistrationsWindow : Window
{
    public ProductRegistrationsWindow(ProductRegistrationsViewModel viewModel)
    {
        InitializeComponent();
        viewModel.RequestClose = Close;
        DataContext = viewModel;
        Closed += (_, _) =>
        {
            viewModel.RequestClose = null;
            DataContext = null;
        };
    }
}
