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

    /// <summary>One sample payment line of a design-mode expense.</summary>
    public sealed record ExpenseInvoiceDesignPayment(string AccountAlias, decimal Amount);

    /// <summary>One sample expense row for the «فواتير المصروفات» preview that runs without SQL.</summary>
    public sealed record ExpenseInvoiceDesignItem(
        string ExpenseNumber,
        DateTime ExpenseDate,
        string CategoryName,
        decimal Amount,
        string Description,
        string ActorUserName,
        IReadOnlyList<ExpenseInvoiceDesignPayment> Payments);

    /// <summary>
    /// Sample expenses for the design preview. Their numbers use the same EXP-YYYY-0000 shape that
    /// dbo.DocumentSequences produces, and one of them is split across two accounts so the preview
    /// shows both the single-account and the split-payment cases.
    /// </summary>
    public static IReadOnlyList<ExpenseInvoiceDesignItem> CreateSampleExpenseInvoices()
    {
        var today = DateTime.Today;
        var year = today.Year;
        return
        [
            new($"EXP-{year}-0001", today, "إيجار", 4500m, "إيجار المخزن لشهر الحالي",
                "admin", [new("الخزينة الرئيسية", 4500m)]),
            new($"EXP-{year}-0002", today.AddDays(-1), "فواتير", 720.5m, "فاتورة كهرباء",
                "admin", [new("الخزينة الرئيسية", 500m), new("فودافون كاش", 220.5m)]),
            new($"EXP-{year}-0003", today.AddDays(-3), "مواصلات", 185m, "انتقالات توصيل الطلبات",
                "admin", [new("فودافون كاش", 185m)]),
            new($"EXP-{year}-0004", today.AddDays(-9), "صيانة", 1250m, "صيانة أجهزة الكاشير",
                "admin", [new("الخزينة الرئيسية", 1250m)]),
            new($"EXP-{year}-0005", today.AddDays(-20), "مرتبات", 9800m, "سلفة مرتب موظف",
                "admin", [new("الخزينة الرئيسية", 6000m), new("إنستاباي", 3800m)])
        ];
    }
}
