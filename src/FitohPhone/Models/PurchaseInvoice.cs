namespace FitohPhone.Models;

public sealed record PurchaseInvoice(
    int Id,
    string InvoiceNumber,
    DateTime InvoiceDate,
    int SupplierId,
    string SupplierName,
    int UserId,
    string UserName,
    decimal SubTotal,
    decimal GrandTotal,
    decimal PaidAmount,
    decimal RemainingAmount,
    string PaymentStatus,
    string? Notes,
    bool IsCancelled,
    DateTime? CancelledAt,
    DateTime CreatedAt,
    IReadOnlyList<PurchaseInvoiceItem> Items,
    IReadOnlyList<PurchaseInvoicePayment> Payments);

public sealed record PurchaseInvoiceItem(
    int Id,
    int InvoiceId,
    int ProductId,
    string ProductBarcode,
    string ProductName,
    int Quantity,
    decimal UnitCost,
    decimal LineTotal);

public sealed record PurchaseInvoicePayment(
    int Id,
    int InvoiceId,
    string PaymentMethod,
    string PaymentMethodAr,
    decimal Amount,
    DateTime PaidAt);

public sealed record PurchaseInvoiceListItem(
    int Id,
    string InvoiceNumber,
    DateTime InvoiceDate,
    string SupplierName,
    string UserName,
    decimal GrandTotal,
    decimal PaidAmount,
    decimal RemainingAmount,
    string PaymentStatus,
    bool IsCancelled);
