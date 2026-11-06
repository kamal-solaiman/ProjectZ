namespace FitohPhone.Models;

public sealed record ShopSetting(
    int Id,
    string ShopNameAr,
    string ShopNameEn,
    string Phone1,
    string? Phone2,
    string? WhatsAppNumber,
    string? Address,
    string? TaxNumber,
    string? CommercialRegisterNumber,
    string? LogoPath,
    string? ReceiptHeaderText,
    string? ReceiptFooterText,
    string DefaultCurrency,
    string? ThermalPrinterName,
    string? BarcodePrinterName,
    bool AutoPrintInvoice,
    bool LowStockAlert,
    bool TransferLimitWarning,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
