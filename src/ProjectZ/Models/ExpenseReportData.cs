namespace ProjectZ.Models;

// The report contract deliberately has no description, counterparty, note or account identifier.
public sealed record ExpenseDocument(int Id, string Reference, DateOnly Day, decimal Amount, int CategoryId,
    string Category, int IssuerId, DateTime RegisteredUtc);
public sealed record ExpenseShare(int Id, int ExpenseId, int AccountId, decimal Amount);
public sealed record ExpensePosting(int Id, int SourceId, int AccountId, DateTime AtUtc, bool Incoming,
    decimal Amount, decimal BalanceAfter, int ActorId)
{
    // Cost convention: a debit to the paying account is positive spending, a credit offsets it.
    public decimal Cost => Incoming ? -Amount : Amount;
}
public sealed record ExpenseReportAccount(int Id, byte Kind, string Alias, bool Active)
{
    public string Label => $"{Kind switch { 0 => "الخزنة", 1 => "فودافون كاش", 2 => "إنستاباي", _ => "حساب" }} · {Alias} (#{Id})" + (Active ? "" : " · موقوف");
}

/// <summary>Match within the source AND account, over the whole document lifetime. Equal net totals alone are not proof.</summary>
public sealed record ExpenseAccountMatch(int AccountId, ExpenseShare? Share, IReadOnlyList<ExpensePosting> Legs, int IssuerId)
{
    public decimal? BookCost { get; } = Legs.Count == 0 ? null : Legs.Sum(l => l.Cost);
    public string Result => (Share, Legs.Count) switch {
        (null, _) => "قيد على حساب غير موزّع عليه",
        (_, 0) => "توزيع بلا قيد خصم",
        (_, > 1) => "أكثر من قيد للحصة الواحدة",
        _ when Legs[0].Incoming => "قيد وارد بدل الخصم",
        _ when Legs[0].Amount != Share!.Amount => "مبلغ القيد يختلف عن الحصة",
        _ when Legs[0].ActorId != IssuerId => "منفّذ القيد يختلف عن مسجّل المستند",
        _ => ExpenseProof.Matched
    };
}
public sealed class ExpenseProof
{
    public const string Matched = "مطابق";
    public IReadOnlyList<ExpenseShare> Shares { get; }
    public IReadOnlyList<ExpenseAccountMatch> Accounts { get; }
    public decimal Allocated { get; }
    public decimal? BookCost { get; }
    public bool AllocationOK { get; }
    public bool OK { get; }
    public string Status => OK ? Matched : "تحتاج مراجعة";

    internal ExpenseProof(ExpenseDocument doc, IEnumerable<ExpenseShare> shares, IEnumerable<ExpensePosting> postings)
    {
        var byAccount = shares.ToDictionary(s => s.AccountId); // Q21 enforces one allocation per (expense, account).
        var legs = postings.ToLookup(l => l.AccountId);
        Shares = byAccount.Values.ToArray();
        Accounts = byAccount.Keys.Union(legs.Select(g => g.Key)).Order().Select(id =>
            new ExpenseAccountMatch(id, byAccount.GetValueOrDefault(id), legs[id].ToArray(), doc.IssuerId)).ToArray();
        Allocated = Shares.Sum(s => s.Amount);
        BookCost = legs.Count == 0 ? null : Accounts.Sum(a => a.BookCost ?? 0m);
        AllocationOK = Shares.Count > 0 && Allocated == doc.Amount;
        OK = AllocationOK && Accounts.All(a => a.Result == Matched);
    }
}

public sealed class ExpenseReportData
{
    public ReportPeriod Period { get; }
    public ReportPeriod Previous { get; }
    public DateTime ReadAtUtc { get; }
    public IReadOnlyDictionary<int, ExpenseDocument> Documents { get; }
    public IReadOnlyList<ExpensePosting> Postings { get; }
    public IReadOnlyDictionary<int, ExpenseProof> Proofs { get; }
    public IReadOnlyDictionary<int, ExpenseReportAccount> Accounts { get; }
    public IReadOnlyDictionary<int, string> Actors { get; }
    public IReadOnlyDictionary<int, string> Categories { get; }

    public static ReportPeriod PreviousOf(ReportPeriod period)
    {
        if (period.From < new DateTime(2, 1, 2)) throw new ArgumentException("اختر فترة تسمح بمقارنة سابقة بنفس عدد الأيام.");
        int days = (period.Through - period.From).Days + 1;
        return new(period.From.AddDays(-days), period.From.AddDays(-1));
    }

    public ExpenseReportData(ReportPeriod period, DateTime readAtUtc, IEnumerable<ExpenseDocument> documents,
        IEnumerable<ExpenseShare> shares, IReadOnlyList<ExpensePosting> postings, IEnumerable<ExpenseReportAccount> accounts,
        IReadOnlyDictionary<int, string> actors)
    {
        Period = period; Previous = PreviousOf(period); ReadAtUtc = readAtUtc;
        Documents = documents.ToDictionary(d => d.Id); Postings = postings; Accounts = accounts.ToDictionary(a => a.Id); Actors = actors;
        Categories = Documents.Values.DistinctBy(d => d.CategoryId).ToDictionary(d => d.CategoryId, d => d.Category);
        var payments = shares.ToLookup(s => s.ExpenseId);
        var book = postings.ToLookup(l => l.SourceId);
        Proofs = Documents.Values.ToDictionary(d => d.Id, d => new ExpenseProof(d, payments[d.Id], book[d.Id]));
    }

    public string CategoryName(int? id) => id is int key ? $"{Categories.GetValueOrDefault(key, "تصنيف")} (#{key})" : "بلا مستند";
    public string ActorName(int id) => $"{Actors.GetValueOrDefault(id, "مستخدم")} (#{id})";
    public string AccountName(int id) => Accounts.GetValueOrDefault(id)?.Label ?? $"حساب #{id}";
    public FinanceLink? AccountLink(int? id) => id is int a && Accounts.GetValueOrDefault(a)?.Active == true ? new(FinanceDestination.Account, a) : null;
}
public interface IExpenseReportSource : IReportSource<ExpenseReportData> { }
