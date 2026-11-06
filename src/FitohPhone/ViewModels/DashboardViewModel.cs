using System.Collections.ObjectModel;
using System.Windows.Input;
using FitohPhone.Data;
using FitohPhone.Helpers;
using FitohPhone.Models;
using FitohPhone.Services;

namespace FitohPhone.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly DashboardRepository _repo;
    private readonly ProductRepository _productRepo;
    private readonly PhoneRepairRepository _repairRepo;
    private readonly ConnectionSettingsProvider _settings;
    private int _userId;

    public DashboardViewModel(DashboardRepository repo, ConnectionSettingsProvider settings, int userId)
    {
        _repo = repo;
        _settings = settings;
        _userId = userId;
        LoadCommand = new AsyncCommand(LoadAsync);
    }

    public AsyncCommand LoadCommand { get; }

    // الإيرادات
    private decimal _salesRevenue; public decimal SalesRevenue { get => _salesRevenue; set { _salesRevenue = value; Changed(); } }
    private decimal _repairRevenue; public decimal RepairRevenue { get => _repairRevenue; set { _repairRevenue = value; Changed(); } }
    private decimal _transferRevenue; public decimal TransferRevenue { get => _transferRevenue; set { _transferRevenue = value; Changed(); } }
    private decimal _expenses; public decimal Expenses { get => _expenses; set { _expenses = value; Changed(); } }
    private decimal _returnsAmount; public decimal ReturnsAmount { get => _returnsAmount; set { _returnsAmount = value; Changed(); } }
    private decimal _totalProfit; public decimal TotalProfit { get => _totalProfit; set { _totalProfit = value; Changed(); } }
    private decimal _netAmount; public decimal NetAmount { get => _netAmount; set { _netAmount = value; Changed(); } }

    // أجهزة الصيانة
    private int _repairsInProgress; public int RepairsInProgress { get => _repairsInProgress; set { _repairsInProgress = value; Changed(); } }
    private int _repairsReady; public int RepairsReady { get => _repairsReady; set { _repairsReady = value; Changed(); } }

    // المخزون
    private int _lowStockCount; public int LowStockCount { get => _lowStockCount; set { _lowStockCount = value; Changed(); } }

    // الأرصدة
    private decimal _cashBalance; public decimal CashBalance { get => _cashBalance; set { _cashBalance = value; Changed(); } }
    private decimal _vodafoneBalance1; public decimal VodafoneBalance1 { get => _vodafoneBalance1; set { _vodafoneBalance1 = value; Changed(); } }
    private decimal _vodafoneBalance2; public decimal VodafoneBalance2 { get => _vodafoneBalance2; set { _vodafoneBalance2 = value; Changed(); } }
    private decimal _instaPayBalance1; public decimal InstaPayBalance1 { get => _instaPayBalance1; set { _instaPayBalance1 = value; Changed(); } }
    private decimal _instaPayBalance2; public decimal InstaPayBalance2 { get => _instaPayBalance2; set { _instaPayBalance2 = value; Changed(); } }

    public decimal VodafoneTotalBalance => VodafoneBalance1 + VodafoneBalance2;
    public decimal InstaPayTotalBalance => InstaPayBalance1 + InstaPayBalance2;

    // الإحصائيات
    private int _totalCustomers; public int TotalCustomers { get => _totalCustomers; set { _totalCustomers = value; Changed(); } }
    private int _totalProducts; public int TotalProducts { get => _totalProducts; set { _totalProducts = value; Changed(); } }
    private int _totalInvoices; public int TotalInvoices { get => _totalInvoices; set { _totalInvoices = value; Changed(); } }

    // قوائم قابلة للربط مع واجهة المستخدم
    public ObservableCollection<RepairDashboardItem> ActiveRepairs { get; } = new();
    public ObservableCollection<LowStockDashboardItem> LowStockItems { get; } = new();

    private bool _busy;
    public bool IsBusy { get => _busy; private set { _busy = value; Changed(); Changed(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;

    public string VodafoneLabel1 => "01012345678";
    public string VodafoneLabel2 => "01098765432";
    public string InstaPayLabel1 => "01100032165";
    public string InstaPayLabel2 => "012985411111";

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var stats = await _repo.GetStatsAsync(System.Threading.CancellationToken.None);
            SalesRevenue = stats.SalesRevenue;
            RepairRevenue = stats.RepairRevenue;
            TransferRevenue = stats.TransferRevenue;
            Expenses = stats.Expenses;
            ReturnsAmount = stats.ReturnsAmount;
            TotalProfit = stats.TotalProfit;
            NetAmount = stats.NetAmount;
            RepairsInProgress = stats.RepairsInProgress;
            RepairsReady = stats.RepairsReady;
            LowStockCount = stats.LowStockCount;
            CashBalance = stats.CashBalance;
            VodafoneBalance1 = stats.VodafoneCashBalance1;
            VodafoneBalance2 = stats.VodafoneCashBalance2;
            InstaPayBalance1 = stats.InstaPayBalance1;
            InstaPayBalance2 = stats.InstaPayBalance2;
            TotalCustomers = stats.TotalCustomers;
            TotalProducts = stats.TotalProducts;
            TotalInvoices = stats.TotalInvoices;

            // جلب الأجهزة النشطة للصيانة
            var repairs = await _repairRepo.GetAllAsync(System.Threading.CancellationToken.None);
            ActiveRepairs.Clear();
            foreach (var r in repairs.Where(r => r.Status == "قيد الصيانة" || r.Status == "جاهز للتسليم").Take(8))
            {
                ActiveRepairs.Add(new RepairDashboardItem(
                    r.RepairNumber,
                    r.CustomerName,
                    r.ProblemDescription,
                    r.Status,
                    r.Status == "قيد الصيانة" ? "#FFF3CD" : "#D4EDDA",
                    r.Status == "قيد الصيانة" ? "#856404" : "#155724"
                ));
            }

            // جلب أصناف الناقص
            var lowStock = await _productRepo.GetLowStockAsync(System.Threading.CancellationToken.None);
            LowStockItems.Clear();
            foreach (var item in lowStock)
            {
                LowStockItems.Add(new LowStockDashboardItem(
                    item.NameAr,
                    item.CurrentStock,
                    item.MinStock
                ));
            }
        }
        finally { IsBusy = false; }
    }
}

public sealed record RepairDashboardItem(
    string RepairNumber,
    string CustomerName,
    string ProblemDescription,
    string StatusAr,
    string StatusColor,
    string StatusTextColor);

public sealed record LowStockDashboardItem(
    string NameAr,
    int CurrentStock,
    int MinStock);
