namespace FitohPhone.Models;

public sealed record ReturnExchange(
    int Id,
    string ReturnNumber,
    DateTime ReturnDate,
    int? OriginalInvoiceId,
    string? OriginalInvoiceNumber,
    int CustomerId,
    string CustomerName,
    string CustomerPhone,
    int UserId,
    string UserName,
    string? Reason,
    decimal TotalAmount,
    decimal RefundAmount,
    decimal ExchangeSettlementAmount,
    string Status,
    string? Notes,
    DateTime CreatedAt,
    IReadOnlyList<ReturnExchangeItem> Items);

public sealed record ReturnExchangeItem(
    int Id,
    int ReturnId,
    int? OriginalInvoiceItemId,
    int ProductId,
    string ProductBarcode,
    string ProductName,
    int Quantity,
    string ItemCondition,
    bool IsDamaged,
    decimal UnitPrice,
    decimal LineTotal,
    int? ReplacementProductId,
    string? ReplacementProductName,
    int? ReplacementQuantity,
    decimal PriceDifference);
