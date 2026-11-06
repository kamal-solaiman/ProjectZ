using System.Windows.Input;
using FitohPhone.Data;
using FitohPhone.Helpers;
using FitohPhone.Models;

namespace FitohPhone.ViewModels;

public sealed class AddProductViewModel : ObservableObject
{
    private readonly ProductRepository _productRepo;
    private readonly CategoryRepository _categoryRepo;
    private readonly BrandRepository _brandRepo;
    private readonly int _userId;

    public AddProductViewModel(ProductRepository productRepo, CategoryRepository categoryRepo,
        BrandRepository brandRepo, int userId)
    {
        _productRepo = productRepo;
        _categoryRepo = categoryRepo;
        _brandRepo = brandRepo;
        _userId = userId;
        SaveCommand = new AsyncCommand(SaveAsync);
        LoadListsCommand = new AsyncCommand(LoadListsAsync);
    }

    public AsyncCommand SaveCommand { get; }
    public AsyncCommand LoadListsCommand { get; }

    public ObservableCollection<ProductCategory> Categories { get; } = new();
    public ObservableCollection<ProductBrand> Brands { get; } = new();
    public ObservableCollection<Product> Products { get; } = new();

    private string _barcode = "";
    public string Barcode { get => _barcode; set { _barcode = value; Changed(); } }

    private string _nameAr = "";
    public string NameAr { get => _nameAr; set { _nameAr = value; Changed(); } }

    private string? _nameEn;
    public string? NameEn { get => _nameEn; set { _nameEn = value; Changed(); } }

    private int? _categoryId;
    public int? CategoryId { get => _categoryId; set { _categoryId = value; Changed(); } }

    private int? _brandId;
    public int? BrandId { get => _brandId; set { _brandId = value; Changed(); } }

    private decimal _costPrice;
    public decimal CostPrice { get => _costPrice; set { _costPrice = value; Changed(); } }

    private decimal _priceWholesale;
    public decimal PriceWholesale { get => _priceWholesale; set { _priceWholesale = value; Changed(); } }

    private decimal _priceSpecial;
    public decimal PriceSpecial { get => _priceSpecial; set { _priceSpecial = value; Changed(); } }

    private decimal _priceRetail;
    public decimal PriceRetail { get => _priceRetail; set { _priceRetail = value; Changed(); } }

    private int _minStock;
    public int MinStock { get => _minStock; set { _minStock = value; Changed(); } }

    private int _currentStock;
    public int CurrentStock { get => _currentStock; set { _currentStock = value; Changed(); } }

    private string _unit = "قطعة";
    public string Unit { get => _unit; set { _unit = value; Changed(); } }

    private int? _warrantyMonths;
    public int? WarrantyMonths { get => _warrantyMonths; set { _warrantyMonths = value; Changed(); } }

    private bool _isActive = true;
    public bool IsActive { get => _isActive; set { _isActive = value; Changed(); } }

    private string _error = "";
    public string Error { get => _error; private set { _error = value; Changed(); } }

    private bool _busy;
    public bool IsBusy { get => _busy; private set { _busy = value; Changed(); Changed(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;

    private async Task LoadListsAsync()
    {
        IsBusy = true;
        try
        {
            Categories.Clear();
            Brands.Clear();
            Products.Clear();
            foreach (var c in await _categoryRepo.GetAllAsync(System.Threading.CancellationToken.None))
                Categories.Add(c);
            foreach (var b in await _brandRepo.GetAllAsync(System.Threading.CancellationToken.None))
                Brands.Add(b);
            foreach (var p in await _productRepo.GetAllAsync(System.Threading.CancellationToken.None))
                Products.Add(p);
        }
        finally { IsBusy = false; }
    }

    private async Task SaveAsync()
    {
        Error = "";
        if (string.IsNullOrWhiteSpace(Barcode) || string.IsNullOrWhiteSpace(NameAr))
        {
            Error = "الباركود واسم المنتج مطلوبان.";
            return;
        }
        var existing = await _productRepo.GetByBarcodeAsync(Barcode, System.Threading.CancellationToken.None);
        if (existing != null)
        {
            Error = "الباركود مسجل مسبقاً لمنتج آخر.";
            return;
        }

        IsBusy = true;
        try
        {
            var product = new Product(
                0, Barcode, NameAr, NameEn, CategoryId, null, BrandId, null,
                CostPrice, PriceWholesale, PriceSpecial, PriceRetail, MinStock,
                CurrentStock, Unit, WarrantyMonths, IsActive, DateTime.Now, null);
            var id = await _productRepo.AddAsync(product, System.Threading.CancellationToken.None);
            // إضافة المنتج المضاف حديثًا إلى قائمة العرض
            var newProduct = await _productRepo.GetByIdAsync(id, System.Threading.CancellationToken.None);
            if (newProduct is not null)
                Products.Insert(0, newProduct);
            // نجاح - يمكن مسح الحقول
            Barcode = ""; NameAr = ""; NameEn = null; CategoryId = null; BrandId = null;
            CostPrice = 0; PriceWholesale = 0; PriceSpecial = 0; PriceRetail = 0;
            MinStock = 0; CurrentStock = 0; Unit = "قطعة"; WarrantyMonths = null;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally { IsBusy = false; }
    }
}
