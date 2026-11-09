using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class EditProductWindow : Window
{
    public EditProductWindow(EditProductViewModel viewModel)
    {
        InitializeComponent();
        viewModel.RequestCloseWithResult = result =>
        {
            DialogResult = result;
            Close();
        };
        DataContext = viewModel;
        Loaded += (_, _) => EditNameInput.Focus();
        Closed += (_, _) =>
        {
            viewModel.RequestCloseWithResult = null;
            DataContext = null;
        };
    }
}
