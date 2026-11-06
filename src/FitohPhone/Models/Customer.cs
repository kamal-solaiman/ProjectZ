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
    DateTime? UpdatedAt)
{
    /// <summary>
    /// نص عرض الرصيد مع علامة المدين/الدائن
    /// </summary>
    public string BalanceText =>
        OutstandingBalance > 0 ? $"عليه {OutstandingBalance:N0} ج" :
        OutstandingBalance < 0 ? $"له {Math.Abs(OutstandingBalance):N0} ج" :
        "مستقر";

    /// <summary>
    /// لون نص الرصيد حسب الحالة
    /// </summary>
    public System.Windows.Media.Brush BalanceColor =>
        OutstandingBalance > 0 ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 152, 0)) :   // برتقالي (مدين)
        OutstandingBalance < 0 ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)) :   // أخضر (دائن)
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(117, 117, 117));                          // رمادي (مستقر)

    /// <summary>
    /// شريحة التسعير الافتراضية للعميل
    /// </summary>
    public string Category =>
        TotalPurchases >= 50000 ? "جملة" :
        TotalPurchases >= 10000 ? "مميز" :
        "قطاعي";
}

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
