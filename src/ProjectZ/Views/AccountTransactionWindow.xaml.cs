using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class AccountTransactionWindow : Window
{
    public AccountTransactionWindow(AccountTransactionViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.RequestClose = () => { DialogResult = vm.Result is not null; Close(); };
        Loaded += (_, _) => AmountInput.Focus();
        Closed += (_, _) => DataContext = null;
    }
}
