using System.Windows;
using FitohPhone.Data;
using FitohPhone.Models;
using FitohPhone.Services;
using FitohPhone.Services;
using FitohPhone.ViewModels;
using FitohPhone.Views;
using FitohPhone.Views.Dialogs;

namespace FitohPhone;

public partial class App : Application
{
    private UserSession? _session;
    private AuthenticationService _authService = null!;
    private readonly ConnectionSettingsProvider _settings = new();
    public ConnectionSettingsProvider SettingsProvider => _settings;

    public static int SessionUserId { get; private set; }

    // Shared repositories (واحدة للتطبيق כולו)
    public UserRepository UserRepo { get; private set; } = null!;
    public CustomerRepository CustomerRepo { get; private set; } = null!;
    public SupplierRepository SupplierRepo { get; private set; } = null!;
    public ProductRepository ProductRepo { get; private set; } = null!;
    public CategoryRepository CategoryRepo { get; private set; } = null!;
    public BrandRepository BrandRepo { get; private set; } = null!;
    public SalesInvoiceRepository SalesInvoiceRepo { get; private set; } = null!;
    public PurchaseInvoiceRepository PurchaseInvoiceRepo { get; private set; } = null!;
    public ReturnExchangeRepository ReturnExchangeRepo { get; private set; } = null!;
    public DamagedStockRepository DamagedStockRepo { get; private set; } = null!;
    public TransferRepository TransferRepo { get; private set; } = null!;
    public PhoneRepairRepository PhoneRepairRepo { get; private set; } = null!;
    public UserRoleRepository UserRoleRepo { get; private set; } = null!;
    public ShopSettingsRepository ShopSettingsRepo { get; private set; } = null!;
    public DashboardRepository DashboardRepo { get; private set; } = null!;
    public AuditLogRepository AuditLogRepo { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // تهيئة جميع المستودعات
        UserRepo = new UserRepository(_settings);
        CustomerRepo = new CustomerRepository(_settings);
        SupplierRepo = new SupplierRepository(_settings);
        ProductRepo = new ProductRepository(_settings);
        CategoryRepo = new CategoryRepository(_settings);
        BrandRepo = new BrandRepository(_settings);
        SalesInvoiceRepo = new SalesInvoiceRepository(_settings);
        PurchaseInvoiceRepo = new PurchaseInvoiceRepository(_settings);
        ReturnExchangeRepo = new ReturnExchangeRepository(_settings);
        DamagedStockRepo = new DamagedStockRepository(_settings);
        TransferRepo = new TransferRepository(_settings);
        PhoneRepairRepo = new PhoneRepairRepository(_settings);
        UserRoleRepo = new UserRoleRepository(_settings);
        ShopSettingsRepo = new ShopSettingsRepository(_settings);
        DashboardRepo = new DashboardRepository(_settings);
        AuditLogRepo = new AuditLogRepository(_settings);

        _authService = new AuthenticationService(UserRepo);
        ShowLogin();
    }

    private void ShowLogin()
    {
        var window = new LoginWindow();
        window.DataContext = new LoginViewModel(_authService, () => window.ReadPassword(), window.ClearPassword,
            user => OpenMain(window, user), window.LifetimeToken);
        MainWindow = window;
        window.Closed += (_, _) => { if (ReferenceEquals(MainWindow, window)) Shutdown(); };
        window.Show();
    }

    private void OpenMain(LoginWindow login, UserSession user)
    {
        _session = user;
        SessionUserId = user.Id;
        var main = new MainWindow();
        main.DataContext = new MainViewModel(user, () =>
        {
            _session = null;
            main.DataContext = null;
            ShowLogin();
            main.Close();
        });
        MainWindow = main;
        main.Closed += (_, _) => { if (ReferenceEquals(MainWindow, main)) { _session = null; Shutdown(); } };
        main.Show();
        login.ClearPassword();
        login.Close();
    }
}
