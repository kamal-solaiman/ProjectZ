using System.Windows.Controls;
using FitohPhone.ViewModels;

namespace FitohPhone.Views.Pages;

public partial class InventoryView : UserControl
{
    private readonly InventoryViewModel _vm;

    public InventoryView()
    {
        InitializeComponent();
        var app = (App)System.Windows.Application.Current;
        _vm = new InventoryViewModel(app.ProductRepo, app.DamagedStockRepo, App.SessionUserId);
        DataContext = _vm;

        // عند تحميل الصفحة، جلب البيانات من قاعدة البيانات
        Loaded += async (_, _) =>
        {
            if (_vm.LoadCommand.CanExecute(null))
                await _vm.LoadCommand.ExecuteAsync(null);
        };
    }
}
