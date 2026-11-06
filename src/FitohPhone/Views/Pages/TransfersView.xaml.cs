using System.Windows;
using System.Windows.Controls;
using FitohPhone.ViewModels;
using FitohPhone.Views.Dialogs;

namespace FitohPhone.Views.Pages;

public partial class TransfersView : UserControl
{
    public TransfersView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        DataContext = new TransfersViewModel(app.TransferRepo, App.SessionUserId);
    }

    private void OpenTransferLine_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TransferLineWindow { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }
}
