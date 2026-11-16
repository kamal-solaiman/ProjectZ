namespace ProjectZ.Models;

/// <summary>One expense section shown by the design preview when the app runs without SQL.</summary>
public sealed record ExpenseCategoryDesignItem(string Name, string Description);

/// <summary>
/// Sample expense sections. The same six rows are seeded once by sql/19_EnableExpenseCategories.sql,
/// so the design preview and a real database start from the same names.
/// </summary>
public static class ExpenseDesignSeedData
{
    public static IReadOnlyList<ExpenseCategoryDesignItem> CreateSampleExpenseCategories() =>
    [
        new("إيجار", "إيجار المحل أو المخزن الشهري"),
        new("فواتير", "كهرباء ومياه وإنترنت واتصالات"),
        new("مرتبات", "أجور الموظفين والمستحقات"),
        new("مواصلات", "انتقالات ونقل وشحن"),
        new("صيانة", "صيانة الأجهزة والمحل"),
        new("أخرى", "مصروفات متنوعة غير مصنفة")
    ];
}
