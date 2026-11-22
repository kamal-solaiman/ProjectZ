using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class AccountEditorWindow : Window
{
    public AccountEditorWindow(AccountEditorViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.RequestClose = () => { DialogResult = vm.Result is not null; Close(); };
        Loaded += (_, _) => AliasInput.Focus();
        Closed += (_, _) => DataContext = null;
    }
}
