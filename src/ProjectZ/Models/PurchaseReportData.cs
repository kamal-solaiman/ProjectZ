namespace ProjectZ.Models;

public sealed record PurchaseReportDocument(int Id, string Number, string SupplierReference, int? SupplierId,
    string Supplier, int? BuyerId, string Buyer, bool CanOpen, bool CanOpenSupplier)
{
    public string NumberLabel => CanOpen ? Number : $"{Number} (محذوف)";
    public string SupplierLabel => SupplierId.HasValue ? $"{Supplier} (#{SupplierId})" : Supplier;
    public string BuyerLabel => BuyerId.HasValue ? $"{Buyer} (#{BuyerId})" : Buyer;
}

public sealed record PurchaseReportInvoice(int Id, DateTime IssuedUtc, DateTime CreatedUtc, byte Status, decimal Gross,
    decimal HeaderDiscount, decimal ActiveGross, decimal Net, decimal Paid, decimal Received, decimal IssuedCredit,
    decimal AppliedToDebt, decimal Available, decimal Due, bool NeedsReview)
{
    public decimal EffectiveDiscount => ActiveGross - Net;
    public static string StatusText(int status) => status switch {
        0 => "مدفوعة بالكامل", 1 => "مدفوعة جزئيًا", 2 => "آجل بالكامل", 3 => "ملغاة", 4 => "مرتجع جزئي", 5 => "مرتجع كلي", _ => "غير معروف" };
}

public sealed record PurchaseReportLine(int InvoiceId, int Position, int ProductId, string Product, string Category,
    string Barcode, string Unit, int Quantity, int Returned, int Kept, decimal UnitCost, decimal ActiveGross, decimal Discount)
{
    public decimal Net => ActiveGross - Discount;
}
public sealed record PurchaseReportReturnLine(int ReturnId, int InvoiceId, string Number, DateTime AtUtc, int ProductId,
    string Product, string Category, string Barcode, int Quantity, decimal UnitCost, long LinkedUnits, long DamagedUnits);
public sealed record PurchaseReportNote(int Id, int InvoiceId, string ReturnNumber, DateTime AtUtc, decimal Amount,
    decimal Applied, decimal Received, decimal Available);

// Matches Kind in dbo.ReadPurchaseReport. PurchasePayments is the only money source.
public enum PurchaseFactKind { Create, Edit, Return, Cancel, Delete, Payment, Receipt, UnverifiedReceipt, CreditIssue, CreditApplication }
public sealed record PurchaseFact(PurchaseFactKind Kind, int SourceId, int Origin, int InvoiceId, int? RelatedId,
    DateTime AtUtc, int? ActorId, string Actor, string Reference, string Detail, decimal? DocumentDelta,
    decimal? DebtReduction, decimal? Amount, int? AccountId, byte? AccountKind, string? Account, int? CreditId,
    int? LedgerId, bool NeedsReview)
{
    public ReportEventKey Key => new((int)Kind, SourceId, Origin);
    public bool ChangesPurchases => Kind is PurchaseFactKind.Create or PurchaseFactKind.Edit or PurchaseFactKind.Return or PurchaseFactKind.Cancel or PurchaseFactKind.Delete;
    public bool IsMoney => Kind is PurchaseFactKind.Payment or PurchaseFactKind.Receipt or PurchaseFactKind.UnverifiedReceipt;
    public string AccountLabel => AccountId is null ? "" : $"{Channel} · {Account} (#{AccountId})";
    public string Channel => AccountKind switch { 0 => "نقدي", 1 => "فودافون كاش", 2 => "إنستاباي", _ => "" };
    public static string KindText(PurchaseFactKind kind) => kind switch {
        PurchaseFactKind.Create => "تسجيل شراء", PurchaseFactKind.Edit => "تعديل مستند", PurchaseFactKind.Return => "مرتجع للمورد",
        PurchaseFactKind.Cancel => "إلغاء شراء", PurchaseFactKind.Delete => "حذف غير مسوّى", PurchaseFactKind.Payment => "سداد للمورد",
        PurchaseFactKind.Receipt => "قبض موثّق", PurchaseFactKind.UnverifiedReceipt => "قبض مسجّل يحتاج مطابقة",
        PurchaseFactKind.CreditIssue => "إصدار إشعار", PurchaseFactKind.CreditApplication => "مقاصة غير نقدية", _ => "غير معروف" };
}
public sealed record PurchaseReportData(ReportPeriod Period, DateTime ReadAtUtc, IReadOnlyDictionary<int,PurchaseReportDocument> Documents,
    IReadOnlyList<PurchaseReportInvoice> Invoices, IReadOnlyList<PurchaseReportLine> Lines, IReadOnlyList<PurchaseFact> Events,
    IReadOnlyList<PurchaseReportReturnLine> ReturnLines, IReadOnlyList<PurchaseReportNote> Notes);
public interface IPurchaseReportSource : IReportSource<PurchaseReportData> { }
