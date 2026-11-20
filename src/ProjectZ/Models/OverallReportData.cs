namespace ProjectZ.Models;

// The SQL contract only permits daily sums by a fixed accounting measure. No entity keys, text or transaction lists.
public enum OverallMeasure : short
{
    SaleIssue=1, SaleReturn=2, SaleReplacement=3, SaleCancel=4, SalesCost=5, SaleReceipts=6, SaleRefunds=7, SalesIssued=8,
    PurchaseCreate=10, PurchaseEdit=11, PurchaseReturn=12, SupplierPaid=13, SupplierReceived=14, DebtReduced=15, CreditIssued=16, CreditUsed=17,
    ServiceIntake=20, Deliveries=21, ServiceAdvance=22, ServiceAtDelivery=23, ServiceRefunds=24, ServiceEarned=25, ServiceParts=26,
    CashIn=30, CashOut=31, VodafoneIn=32, VodafoneOut=33, InstaIn=34, InstaOut=35, FeeIssued=36, FeeReversed=37,
    ExpenseWritten=40, ExpenseDebit=41, ExpenseCredit=42, Damage=50, Restored=51, Compensation=52
}
public sealed record DailyFigure(DateOnly Day, OverallMeasure Measure, decimal Known, long Missing, long Count);
public sealed record ChannelBalance(byte Kind,long Accounts,decimal Amount);
public sealed record OverallReportData(ReportPeriod Period,DateTime ReadAtUtc,IReadOnlyList<DailyFigure> Figures,IReadOnlyList<ChannelBalance> Balances);
public interface IOverallReportSource : IReportSource<OverallReportData> { }

/// <summary>An absent bucket in a successful snapshot is zero; an unvalued contributing fact is NOT zero.</summary>
internal sealed class OperatingTotals(IEnumerable<DailyFigure> figures)
{
    private readonly Dictionary<OverallMeasure,(decimal Sum,long Missing,long Count)> totals = figures.GroupBy(f=>f.Measure)
        .ToDictionary(g=>g.Key,g=>(g.Sum(f=>f.Known),g.Sum(f=>f.Missing),g.Sum(f=>f.Count)));
    public decimal? this[OverallMeasure key] { get { var s=totals.GetValueOrDefault(key); return s.Missing==0?s.Sum:null; } }
    public long Count(OverallMeasure key)=>totals.GetValueOrDefault(key).Count;
    public bool HasActivity=>totals.Count>0;
    public decimal? NetSales => this[OverallMeasure.SaleIssue]+this[OverallMeasure.SaleReplacement]-this[OverallMeasure.SaleReturn]-this[OverallMeasure.SaleCancel];
    public decimal? GrossProfit => NetSales-this[OverallMeasure.SalesCost];
    public decimal? Service => this[OverallMeasure.ServiceEarned]-this[OverallMeasure.ServiceParts];
    public decimal? Fees => this[OverallMeasure.FeeIssued]-this[OverallMeasure.FeeReversed];
    public decimal? NetDamage => this[OverallMeasure.Damage]-this[OverallMeasure.Restored]-this[OverallMeasure.Compensation];
    public decimal? Profit => GrossProfit+Service+Fees-this[OverallMeasure.ExpenseWritten]-NetDamage;
    public decimal? Income => NetSales+this[OverallMeasure.ServiceEarned]+Fees;
    public decimal? Inflow => this[OverallMeasure.CashIn]+this[OverallMeasure.VodafoneIn]+this[OverallMeasure.InstaIn];
    public decimal? Outflow => this[OverallMeasure.CashOut]+this[OverallMeasure.VodafoneOut]+this[OverallMeasure.InstaOut];
    public decimal? CashChange => Inflow-Outflow;
    public string Verdict => Profit switch { null=>"نتيجة غير مكتملة", >0=>"ربح تشغيلي", <0=>"خسارة تشغيلية", _=>HasActivity?"تعادل تشغيلي":"لا نشاط مسجّل" };
}
