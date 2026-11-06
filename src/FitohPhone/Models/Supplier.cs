namespace FitohPhone.Models;

public sealed record Supplier(
    int Id,
    string Name,
    string Phone,
    string? ContactPerson,
    string? Address,
    string? City,
    string? Email,
    decimal TotalPurchases,
    decimal TotalPaid,
    decimal OutstandingBalance,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
