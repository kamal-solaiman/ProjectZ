namespace FitohPhone.Models;

public sealed record Product(
    int Id,
    string Barcode,
    string NameAr,
    string? NameEn,
    int? CategoryId,
    string? CategoryName,
    int? BrandId,
    string? BrandName,
    decimal CostPrice,
    decimal PriceWholesale,
    decimal PriceSpecial,
    decimal PriceRetail,
    int MinStock,
    int CurrentStock,
    string Unit,
    int? WarrantyMonths,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record ProductStockItem(
    int Id,
    string Barcode,
    string NameAr,
    string CategoryName,
    string BrandName,
    decimal CostPrice,
    decimal PriceRetail,
    int CurrentStock,
    int MinStock,
    bool IsLowStock);
