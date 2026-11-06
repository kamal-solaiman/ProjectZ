namespace FitohPhone.Models;

public sealed record SalesInvoice(
    int Id,
    string InvoiceNumber,
    DateTime InvoiceDate,
    int? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    int UserId,
    string UserName,
    decimal SubTotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal GrandTotal,
    decimal PaidAmount,
    decimal RemainingAmount,
    string PaymentStatus,
    string PriceTier,
    string? Notes,
    bool IsCancelled,
    DateTime? CancelledAt,
    DateTime CreatedAt,
    IReadOnlyList<SalesInvoiceItem> Items,
    IReadOnlyList<SalesInvoicePayment> Payments);

public sealed record SalesInvoiceItem(
    int Id,
    int InvoiceId,
    int ProductId,
    string ProductBarcode,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal LineTotal,
    string PriceTierUsed);

public sealed record SalesInvoicePayment(
    int Id,
    int InvoiceId,
    string PaymentMethod,
    string PaymentMethodAr,
    decimal Amount,
    string? ReferenceNumber,
    DateTime PaidAt);

public sealed record SalesInvoiceListItem(
    int Id,
    string InvoiceNumber,
    DateTime InvoiceDate,
    string? CustomerName,
    string? CustomerPhone,
    string UserName,
    decimal GrandTotal,
    decimal PaidAmount,
    decimal RemainingAmount,
    string PaymentStatus,
    bool IsCancelled);
