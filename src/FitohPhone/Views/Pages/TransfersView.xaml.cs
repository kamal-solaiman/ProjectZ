using System.Windows;
using System.Windows.Controls;
using FitohPhone.Views.Dialogs;

namespace FitohPhone.Views.Pages;

public partial class TransfersView : UserControl
{
    public TransfersView() => InitializeComponent();

    private void OpenTransferLine_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TransferLineWindow { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }
}
