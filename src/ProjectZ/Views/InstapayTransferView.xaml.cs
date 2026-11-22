using System.Windows;
using System.Windows.Controls;
using ProjectZ.Models;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class InstapayTransferView : UserControl
{
    public InstapayTransferView() => InitializeComponent();

    private void Deposit_Click(object sender, RoutedEventArgs e) => OpenOperation(sender, true);
    private void Withdraw_Click(object sender, RoutedEventArgs e) => OpenOperation(sender, false);

    private void OpenOperation(object sender, bool deposit)
    {
        if (DataContext is not InstapayTransferViewModel viewModel) return;
        if (sender is FrameworkElement { DataContext: FinancialAccountDesignItem wallet })
            viewModel.SelectWalletCommand.Execute(wallet);
        viewModel.SelectOperationTypeCommand.Execute(deposit ? "إيداع للعميل" : "سحب للعميل");
        var window = new InstapayOperationWindow { Owner = Window.GetWindow(this), DataContext = viewModel };
        window.ShowDialog();
    }
}
