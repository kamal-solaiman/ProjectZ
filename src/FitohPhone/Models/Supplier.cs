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
    DateTime? UpdatedAt)
{
    /// <summary>
    /// نص عرض الرصيد للمورد
    /// </summary>
    public string BalanceText =>
        OutstandingBalance > 0 ? $"له {OutstandingBalance:N0} ج" :
        OutstandingBalance < 0 ? $"عليه {Math.Abs(OutstandingBalance):N0} ج" :
        "مستقر";

    /// <summary>
    /// لون نص الرصيد للمورد
    /// </summary>
    public System.Windows.Media.Brush BalanceColor =>
        OutstandingBalance > 0 ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 152, 0)) :   // برتقالي
        OutstandingBalance < 0 ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)) :   // أخضر
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(117, 117, 117));                         // رمادي
}
