using System.Windows.Controls;
using FitohPhone.ViewModels;

namespace FitohPhone.Views.Pages;

public partial class AddProductView : UserControl
{
    public AddProductView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        DataContext = new AddProductViewModel(app.ProductRepo, app.CategoryRepo, app.BrandRepo, App.SessionUserId);
    }
}
