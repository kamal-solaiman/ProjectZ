using System.Windows;

namespace FitohPhone.Views.Dialogs;

public partial class TransferLineWindow : Window
{
    public TransferLineWindow() => InitializeComponent();
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
}
