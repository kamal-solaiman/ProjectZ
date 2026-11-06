namespace FitohPhone.Models;

public sealed record Customer(
    int Id,
    string Name,
    string Phone,
    string? AlternatePhone,
    string? Address,
    string? City,
    string? Email,
    decimal TotalPurchases,
    decimal TotalReturns,
    decimal OutstandingBalance,
    DateTime? LastVisit,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record CustomerSummary(
    int Id,
    string Name,
    string Phone,
    decimal TotalPurchases,
    decimal TotalReturns,
    decimal OutstandingBalance,
    int InvoiceCount,
    int RepairCount,
    int ReturnCount);
