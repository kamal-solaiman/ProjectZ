using System.Windows.Input;
using FitohPhone.Data;
using FitohPhone.Helpers;
using FitohPhone.Models;

namespace FitohPhone.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly ShopSettingsRepository _shopRepo;
    private readonly int _userId;

    public SettingsViewModel(ShopSettingsRepository shopRepo, int userId)
    {
        _shopRepo = shopRepo;
        _userId = userId;
        LoadCommand = new AsyncCommand(LoadAsync);
        SaveCommand = new AsyncCommand(SaveAsync);
    }

    public AsyncCommand LoadCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public bool SaveSucceeded { get; private set; }

    private string _shopNameAr = "";
    public string ShopNameAr { get => _shopNameAr; set { _shopNameAr = value; Changed(); } }

    private string _shopNameEn = "";
    public string ShopNameEn { get => _shopNameEn; set { _shopNameEn = value; Changed(); } }

    private string _phone1 = "";
    public string Phone1 { get => _phone1; set { _phone1 = value; Changed(); } }

    private string? _phone2;
    public string? Phone2 { get => _phone2; set { _phone2 = value; Changed(); } }

    private string? _whatsapp;
    public string? WhatsApp { get => _whatsapp; set { _whatsapp = value; Changed(); } }

    private string? _address;
    public string? Address { get => _address; set { _address = value; Changed(); } }

    private string? _taxNumber;
    public string? TaxNumber { get => _taxNumber; set { _taxNumber = value; Changed(); } }

    private string? _commercialReg;
    public string? CommercialReg { get => _commercialReg; set { _commercialReg = value; Changed(); } }

    private string _defaultCurrency = "ج.م (EGP)";
    public string DefaultCurrency { get => _defaultCurrency; set { _defaultCurrency = value; Changed(); } }

    private string? _thermalPrinter;
    public string? ThermalPrinter { get => _thermalPrinter; set { _thermalPrinter = value; Changed(); } }

    private string? _barcodePrinter;
    public string? BarcodePrinter { get => _barcodePrinter; set { _barcodePrinter = value; Changed(); } }

    private bool _autoPrint = true;
    public bool AutoPrintInvoice { get => _autoPrint; set { _autoPrint = value; Changed(); } }

    private bool _lowStockAlert = true;
    public bool LowStockAlert { get => _lowStockAlert; set { _lowStockAlert = value; Changed(); } }

    private bool _transferLimitWarning = true;
    public bool TransferLimitWarning { get => _transferLimitWarning; set { _transferLimitWarning = value; Changed(); } }

    private string _error = "";
    public string Error { get => _error; private set { _error = value; Changed(); } }

    private string _message = "";
    public string Message { get => _message; private set { _message = value; Changed(); } }

    private bool _busy;
    public bool IsBusy { get => _busy; private set { _busy = value; Changed(); Changed(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var setting = await _shopRepo.GetAsync(System.Threading.CancellationToken.None);
            if (setting is null) return;
            ShopNameAr = setting.ShopNameAr;
            ShopNameEn = setting.ShopNameEn;
            Phone1 = setting.Phone1;
            Phone2 = setting.Phone2;
            WhatsApp = setting.WhatsAppNumber;
            Address = setting.Address;
            TaxNumber = setting.TaxNumber;
            CommercialReg = setting.CommercialRegisterNumber;
            DefaultCurrency = setting.DefaultCurrency;
            ThermalPrinter = setting.ThermalPrinterName;
            BarcodePrinter = setting.BarcodePrinterName;
            AutoPrintInvoice = setting.AutoPrintInvoice;
            LowStockAlert = setting.LowStockAlert;
            TransferLimitWarning = setting.TransferLimitWarning;
        }
        finally { IsBusy = false; }
    }

    private async Task SaveAsync()
    {
        Error = "";
        Message = "";
        SaveSucceeded = false;
        if (string.IsNullOrWhiteSpace(ShopNameAr) || string.IsNullOrWhiteSpace(Phone1))
        {
            Error = "اسم المحل والهاتف الأساسي مطلوبان.";
            return;
        }

        IsBusy = true;
        try
        {
            var setting = new ShopSetting(
                1, ShopNameAr, ShopNameEn, Phone1, Phone2, WhatsApp, Address,
                TaxNumber, CommercialReg, null, null, null, DefaultCurrency,
                ThermalPrinter, BarcodePrinter, AutoPrintInvoice, LowStockAlert,
                TransferLimitWarning, DateTime.Now, null);
            var ok = await _shopRepo.UpdateAsync(setting, System.Threading.CancellationToken.None);
            SaveSucceeded = ok;
            Message = ok ? "تم حفظ إعدادات المحل بنجاح." : "فشل الحفظ.";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally { IsBusy = false; }
    }
}
