using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class BarcodePrintPreviewWindow : Window
{
    public BarcodePrintPreviewWindow(BarcodePrintPreviewViewModel viewModel)
    {
        InitializeComponent();
        viewModel.RequestClose = Close;
        DataContext = viewModel;
        Loaded += (_, _) =>
        {
            CopiesInput.Focus();
            CopiesInput.SelectAll();
        };
        Closed += (_, _) =>
        {
            viewModel.RequestClose = null;
            DataContext = null;
        };
    }
}
