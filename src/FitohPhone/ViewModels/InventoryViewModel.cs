using System.Collections.ObjectModel;
using System.Windows.Input;
using FitohPhone.Data;
using FitohPhone.Helpers;
using FitohPhone.Models;

namespace FitohPhone.ViewModels;

public sealed class InventoryViewModel : ObservableObject
{
    private readonly ProductRepository _productRepo;
    private readonly DamagedStockRepository _damagedRepo;
    private readonly int _userId;

    public InventoryViewModel(ProductRepository productRepo, DamagedStockRepository damagedRepo, int userId)
    {
        _productRepo = productRepo;
        _damagedRepo = damagedRepo;
        _userId = userId;
        LoadCommand = new AsyncCommand(LoadAsync);
        RefreshCommand = new AsyncCommand(LoadAsync);
        EditItemCommand = new RelayCommand<int>(EditItem);
        StockAdjustCommand = new RelayCommand<int>(AdjustStock);
        DeleteItemCommand = new RelayCommand<int>(DeleteItem);
    }

    public AsyncCommand LoadCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public RelayCommand<int> EditItemCommand { get; }
    public RelayCommand<int> StockAdjustCommand { get; }
    public RelayCommand<int> DeleteItemCommand { get; }

    public ObservableCollection<ProductStockItem> StockItems { get; } = new();
    public ObservableCollection<DamagedStockItem> DamagedItems { get; } = new();

    private string _searchText = "";
    public string SearchText { get => _searchText; set { _searchText = value; Changed(); ApplyFilter(); } }

    private int _totalItems; public int TotalItems { get => _totalItems; set { _totalItems = value; Changed(); } }
    private decimal _totalStockValue; public decimal TotalStockValue { get => _totalStockValue; set { _totalStockValue = value; Changed(); } }
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
            TotalStockValue = StockItems.Sum(i => i.CostPrice * i.CurrentStock);
            LowStockCount = low.Count;

            // تحميل التالف من قاعدة البيانات
            var damaged = await _damagedRepo.GetAllAsync(System.Threading.CancellationToken.None);
            foreach (var d in damaged)
            {
                DamagedItems.Add(new DamagedStockItem(
                    d.ProductName, d.Quantity, d.DamageReason ?? "غير محدد",
                    d.DamageDate, d.LossValue));
            }
            DamagedCount = DamagedItems.Count;

            ApplyFilter();
        }
        finally { IsBusy = false; }
    }

    private void ApplyFilter()
    {
        var term = SearchText?.ToLower() ?? "";
        // التصفية البسيطة على StockItems (يمكن تحسينها بـ CollectionView später)
        // في هذه النسخة، نعرض جميع الأصناف ويومّن البحث اليدوي
    }

    private void EditItem(int productId)
    {
        // يمكن فتح نافذة تعديل المنتج لاحقًا
    }

    private void AdjustStock(int productId)
    {
        // يمكن فتح نافذة تسوية المخزون لاحقًا
    }

    private void DeleteItem(int productId)
    {
        // يمكن حذف المنتج لاحقًا مع تأكيد
    }
}

public sealed record DamagedStockItem(
    string ProductName,
    int Quantity,
    string DamageReason,
    DateTime DamageDate,
    decimal LossValue);
