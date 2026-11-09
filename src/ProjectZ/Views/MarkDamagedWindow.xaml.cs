using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class MarkDamagedWindow : Window
{
    public MarkDamagedWindow(MarkDamagedViewModel viewModel)
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
            if (DamagedQtyInput.IsEnabled)
            {
                DamagedQtyInput.Focus();
                DamagedQtyInput.SelectAll();
            }
        };
        Closed += (_, _) =>
        {
            viewModel.RequestCloseWithResult = null;
            DataContext = null;
        };
    }
}
