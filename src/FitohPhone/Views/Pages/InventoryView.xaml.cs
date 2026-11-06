using System.Windows.Controls;
using FitohPhone.ViewModels;

namespace FitohPhone.Views.Pages;

public partial class InventoryView : UserControl
{
    public InventoryView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        DataContext = new InventoryViewModel(app.ProductRepo, App.SessionUserId);
    }
}
