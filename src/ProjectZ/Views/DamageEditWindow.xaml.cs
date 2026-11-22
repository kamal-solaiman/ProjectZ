using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class DamageEditWindow : Window
{
    public DamageEditWindow(DamageEditViewModel viewModel)
    {
        InitializeComponent();
        viewModel.RequestCloseWithResult = result =>
        {
            DialogResult = result;
            Close();
        };
        DataContext = viewModel;
        Closed += (_, _) =>
        {
            viewModel.RequestCloseWithResult = null;
            DataContext = null;
        };
    }
}
