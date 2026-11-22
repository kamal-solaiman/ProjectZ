using System.Windows;
using ProjectZ.ViewModels;

namespace ProjectZ.Views;

public partial class InstapayRecordEditWindow : Window
{
    public InstapayRecordEditWindow() => InitializeComponent();
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is InstapayRecordsViewModel vm && vm.ApplyEdit())
        {
            DialogResult = true;
            Close();
        }
    }
}
