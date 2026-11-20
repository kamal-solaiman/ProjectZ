using Microsoft.Data.SqlClient;
using ProjectZ.Data;
using ProjectZ.Models;
using ProjectZ.Services;
using static SqlTest;
using static ProjectZ.Models.OverallMeasure;

internal static partial class OverallReportTests
{
    internal static void Run(SqlConnection db, ConnectionSettingsProvider settings,int actor,int cash)
    {
        var repo=new OverallReportRepository(settings);
        OverallReportData Read(ReportPeriod p)=>repo.LoadAsync(p,CancellationToken.None).GetAwaiter().GetResult();
        OperatingTotals Current(OverallReportData d)=>new(d.Figures.Where(f=>f.Day>=DateOnly.FromDateTime(d.Period.From)));
        var now=new ReportPeriod(ReportPeriod.Today.AddDays(-1),ReportPeriod.Today.AddDays(1));
        var before=PurchaseSettlementTests.Footprint(db)+MaintenanceSettlementTests.Footprint(db);
        var d=Read(now); var s=Current(d);
        var sales=new SalesReportRepository(settings).LoadAsync(now,CancellationToken.None).GetAwaiter().GetResult();
        var activity=sales.Events.Where(e=>now.Contains(e.AtUtc)).ToArray();
        Check(s[SaleReceipts]==activity.Sum(e=>e.Received) && s[SaleRefunds]==activity.Sum(e=>e.Refunded),"Summary agrees with actual dated sales receipts, never item count times money");
        var finance=new FinanceReportRepository(settings).LoadAsync(now,CancellationToken.None).GetAwaiter().GetResult();
        Check(s.CashChange==finance.Legs.Sum(l=>l.Delta) && d.Balances.Sum(b=>b.Amount)==finance.Accounts.Values.Sum(a=>a.Current),"Five-report cash flow uses the book once; balances at read time are separate");
        Check(before==PurchaseSettlementTests.Footprint(db)+MaintenanceSettlementTests.Footprint(db),"Overall reporting does not update any operational data");
        var feeDay=new DateTime(2041,3,8);
        Check(Current(Read(new(feeDay,feeDay))).Fees==10 && Current(Read(new(feeDay.AddDays(1),feeDay.AddDays(1)))).Fees==-8
            && Current(Read(new(feeDay.AddDays(2),feeDay.AddDays(2)))).Profit==null,"Only matched commissions earn profit, +10 then -8; an orphan leaves result incomplete");
        var expenseDay=new DateTime(2037,3,1);var expense=Current(Read(new(expenseDay,expenseDay)));
        Check(expense[ExpenseWritten]==310.55m && expense[ExpenseDebit]==10.55m,"Backdated split expenses are charged once to the written date, not the account debit date");
        Check(d.ExpenseGroups.Sum(g=>g.Current)==s[ExpenseWritten] && Current(Read(new(feeDay,feeDay))).CashChange==10,
            "Expense categories reconcile to the header once, portfolio transfer legs net separately to 10");
        SalesAndDamage(db,settings,actor,cash);
        DeliveriesAndReopens(db,settings,actor);
        using var till=new SqlConnection(settings.GetConnectionString()); till.Open();
        Check(Scalar<int>(till,"SELECT HAS_PERMS_BY_NAME('dbo.ReadOverallReport','OBJECT','EXECUTE');")==1
            && Scalar<int>(till,"SELECT ISNULL(HAS_PERMS_BY_NAME('dbo.OverallRemovedDamage','OBJECT','INSERT'),0);")==0,"Till can read aggregates but cannot manufacture archived damage");
        using var cmd=new SqlCommand("dbo.ReadOverallReport",till){CommandType=System.Data.CommandType.StoredProcedure};
        cmd.Parameters.AddWithValue("@FromDate",now.From);cmd.Parameters.AddWithValue("@ThroughDate",now.Through);
        using var reader=cmd.ExecuteReader(); int sets=0;
        string[][] expected=[["Day","Measure","KnownAmount","Missing","FactCount"],["AccountKind","Accounts","Amount"],["Category","CurrentExpense","PreviousExpense"],["ReadAtUtc"]];
        do { Check(sets<expected.Length && Enumerable.Range(0,reader.FieldCount).Select(reader.GetName).SequenceEqual(expected[sets++]),"Overall SQL contract consists only of aggregate columns");
            while(reader.Read()) for(int n=0;n<reader.FieldCount;n++)if(reader.GetFieldType(n)==typeof(string) && (reader.GetName(n)!="Category" || reader.GetString(n).Contains("PRIVATE",StringComparison.OrdinalIgnoreCase)))throw new Exception("Unexpected private text in overall report"); }while(reader.NextResult());
        Check(sets==4,"No detailed documents or additional result sets reach the cashier");
    }
}
