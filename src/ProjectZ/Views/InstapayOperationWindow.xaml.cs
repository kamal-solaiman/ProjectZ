using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class InstapayOperationWindow : Window
{
    public InstapayOperationWindow() => InitializeComponent();

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Execute_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not InstapayTransferViewModel viewModel) return;
        viewModel.ExecuteOperationCommand.Execute(null);
        if (!viewModel.IsErrorStatus)
        {
            DialogResult = true;
            Close();
        }
    }
}
