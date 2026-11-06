using System.Collections.ObjectModel;
using System.Windows.Input;
using FitohPhone.Data;
using FitohPhone.Helpers;
using FitohPhone.Models;

namespace FitohPhone.ViewModels;

public sealed class InventoryViewModel : ObservableObject
{
    private readonly ProductRepository _productRepo;
    private readonly int _userId;

    public InventoryViewModel(ProductRepository productRepo, int userId)
    {
        _productRepo = productRepo;
        _userId = userId;
        LoadCommand = new AsyncCommand(LoadAsync);
        RefreshCommand = new AsyncCommand(LoadAsync);
    }

    public AsyncCommand LoadCommand { get; }
    public AsyncCommand RefreshCommand { get; }

    public ObservableCollection<ProductStockItem> StockItems { get; } = new();
    public ObservableCollection<ProductStockItem> DamagedItems { get; } = new();

    private string _searchText = "";
    public string SearchText { get => _searchText; set { _searchText = value; Changed(); } }

    private int _totalItems; public int TotalItems { get => _totalItems; set { _totalItems = value; Changed(); } }
    private int _lowStockCount; public int LowStockCount { get => _lowStockCount; set { _lowStockCount = value; Changed(); } }
    private int _damagedCount; public int DamagedCount { get => _damagedCount; set { _damagedCount = value; Changed(); } }

    private bool _busy;
    public bool IsBusy { get => _busy; private set { _busy = value; Changed(); Changed(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var all = await _productRepo.GetAllAsync(System.Threading.CancellationToken.None);
            var low = await _productRepo.GetLowStockAsync(System.Threading.CancellationToken.None);

            StockItems.Clear();
            DamagedItems.Clear();

            foreach (var p in all)
            {
                var item = new ProductStockItem(
                    p.Id, p.Barcode, p.NameAr, p.CategoryName ?? "غير محدد",
                    p.BrandName ?? "غير محدد", p.CostPrice, p.PriceRetail,
                    p.CurrentStock, p.MinStock, p.CurrentStock <= p.MinStock);
                StockItems.Add(item);
            }

            TotalItems = StockItems.Count;
            LowStockCount = low.Count;

            // التالف يُعرض بشكل منفصل (في الشاشة الحقيقية يُجلب من جدول DamagedStock)
            DamagedCount = 0;
        }
        finally { IsBusy = false; }
    }

    public void Filter()
    {
        var term = SearchText?.ToLower() ?? "";
        // التصفية تُطبق على مستوى الـ View (يمكن تحسينها لاحقاً)
    }

    public async Task AdjustStockAsync(int productId, int newQuantity, CancellationToken ct)
    {
        var item = StockItems.FirstOrDefault(i => i.Id == productId);
        if (item is null) return;
        var change = newQuantity - item.CurrentStock;
        if (change == 0) return;
        await _productRepo.UpdateStockAsync(productId, change, ct);
        await LoadAsync();
    }
}
