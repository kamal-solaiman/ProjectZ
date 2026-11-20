using System.Data;
using Microsoft.Data.SqlClient;
using ProjectZ.Data;
using ProjectZ.Models;
using ProjectZ.Services;
using static SqlTest;

internal static class ExpenseReportTests
{
    private static string Footprint(SqlConnection db) => WalletCorrectionTests.Footprint(db) + Scalar<string>(db,"""
        SELECT CONCAT((SELECT * FROM dbo.Expenses ORDER BY Id FOR JSON PATH),(SELECT * FROM dbo.ExpensePayments ORDER BY Id FOR JSON PATH),
            (SELECT * FROM dbo.ExpenseCategories ORDER BY Id FOR JSON PATH),(SELECT * FROM dbo.DocumentSequences ORDER BY DocumentType,SequenceYear FOR JSON PATH));
        """);

    internal static void RefuseLegacySchema(SqlConnection db, string installer)
    {
        var errors = new List<int>();
        foreach (var batch in Batches(File.ReadAllText(installer)))
        {
            try { Execute(db,batch); } catch (SqlException ex) { errors.Add(ex.Number); }
        }
        Check(errors.SequenceEqual([50268]) && Scalar<int>(db,"SELECT COUNT(*) FROM sys.procedures WHERE name=N'ReadExpenseReport'")==0
            && Scalar<int>(db,"SELECT COUNT(*) FROM sys.indexes WHERE name=N'IX_FinancialTransactions_SourceDate'")==0,
            "Expense report refuses the unmigrated single-account schema without partially installing");
    }

    internal static void Run(SqlConnection db, ConnectionSettingsProvider settings, int actor, int cash, string installer)
    {
        const string secret = "PRIVATE-PAYROLL-Q30", phone = "01076549876", address = "private-expense@instapay";
        var reader = new ExpenseReportRepository(settings); var writer = new ExpenseRepository(settings,actor);
        ExpenseReportData Read(ReportPeriod period) => reader.LoadAsync(period,CancellationToken.None).GetAwaiter().GetResult();
        ReportPage Page(ExpenseReportData d, bool posted = false, ReportFilter? f = null, int? category = null) =>
            ExpenseReportProjection.Build(d,posted,f ?? new(),"EGP",category);
        decimal? Metric(ReportPage p, string name) => p.Metrics.Single(m => m.Label == name).Value;
        ReportTable Table(ReportPage p, string name) => p.Tables.Single(t => t.Title == name);
        var day = new DateTime(2037,3,1); var period = new ReportPeriod(day,day); var next = new ReportPeriod(day.AddDays(1),day.AddDays(1));
        var previous = ExpenseReportData.PreviousOf(period);
        var finance = new FinancialRepository(settings,actor);
        int wallet = finance.SaveAccount(null,FinancialAccountKind.InstaPay,address,address,1900,0,0,0,0,secret,false).AccountId;
        int category1 = writer.LoadCategories().First().Id;
        int category2 = Scalar<int>(db,"INSERT dbo.ExpenseCategories(Name,Description) OUTPUT inserted.Id VALUES(N'رسوم سابقة',@secret);",("@secret",secret));
        int category3 = Scalar<int>(db,"INSERT dbo.ExpenseCategories(Name) OUTPUT inserted.Id VALUES(N'رسوم جديدة');");
        SavedExpense Save(int category, DateTime written, DateTime postedAt, params ExpensePaymentRequest[] shares)
        {
            var saved = writer.Save(new(category,DateTime.Today.AddDays(-3),shares.Sum(s => s.Amount),secret+phone,shares));
            Execute(db,"""
                UPDATE dbo.Expenses SET ExpenseDate=@written WHERE Id=@id;
                UPDATE dbo.FinancialTransactions SET OccurredAtUtc=@at,Counterparty=@phone WHERE SourceEntityType=N'Expense' AND SourceEntityId=@id;
                """,("@written",written),("@id",saved.ExpenseId),("@at",postedAt),("@phone",phone));
            return saved;
        }
        var split = Save(category1,day,next.StartUtc,new ExpensePaymentRequest(cash,120),new(wallet,180));
        Save(category1,previous.From,previous.StartUtc,new ExpensePaymentRequest(cash,100));
        Save(category2,previous.From,previous.StartUtc,new ExpensePaymentRequest(wallet,40));
        Save(category3,day,period.StartUtc,new ExpensePaymentRequest(wallet,10.55m));
        var earlier = Save(category1,day.AddDays(-14),next.StartUtc.AddSeconds(1),new ExpensePaymentRequest(wallet,2));
        finance.SetAccountActive(wallet,false);
        var before = Footprint(db);
        foreach (var batch in Batches(File.ReadAllText(installer))) Execute(db,batch); // repeat on populated data
        var one = Read(period); var two = Read(next); var docs = Page(one); var dated = Page(one,true); var later = Page(two,true);
        Check(one.Proofs[split.ExpenseId].OK && Metric(docs,"إجمالي المستندات")==310.55m && Metric(docs,"مستندات")==2
            && Metric(dated,"صافي الخصم")==10.55m && Metric(later,"صافي الخصم")==302,
            "Written date and Egypt posting instant select different facts: split expense counted once, not on yesterday's cash ledger");
        var cashDocs = Page(one,f:new(AccountId:cash)); var cashLegs = Page(two,true,new(AccountId:cash));
        Check(Metric(cashDocs,"إجمالي المستندات")==300 && Metric(cashDocs,"حصص الحسابات المعروضة")==120 && Table(cashDocs,"توزيع الدفع").Rows.Count==1
            && Metric(cashLegs,"منصرف المصروفات")==120 && cashLegs.Tables[0].Rows.Single().FinancialSource == new FinanceLink(FinanceDestination.Expense,split.ExpenseId)
            && cashLegs.Tables[0].Rows.Single().FinancialAccount == new FinanceLink(FinanceDestination.Account,cash),
            "Filtering cash keeps one full 300 document but only its 120 allocation / actual posting; destinations have independent keys");
        Check(later.Tables[0].Rows.Any(r => r.FinancialSource?.Id==earlier.ExpenseId) && !two.Accounts[wallet].Active
            && later.Tables[0].Rows.Where(r => r.Display.Contains(two.AccountName(wallet))).All(r => r.FinancialAccount==null),
            "A posted expense can come from outside BOTH document ranges, including an inactive account");
        var cmp = Table(docs,ExpenseReportProjection.CompareCategory).Rows;
        var growing = cmp.Single(r => r.Display[0]==one.CategoryName(category1));
        Check(Equals(growing.Cells[2],300m) && Equals(growing.Cells[4],100m) && Equals(growing.Cells[5],200m) && Equals(growing.Cells[6],200m)
            && cmp.Single(r => r.Display[0]==one.CategoryName(category2)).Display.Contains("لا نشاط حالي")
            && cmp.Single(r => r.Display[0]==one.CategoryName(category3)) is var fresh && fresh.Cells[6]==null && fresh.Display.Contains("جديد"),
            "Equal period comparison uses the union of category IDs including prior-only and never divides new spending by zero");
        var oldId = Scalar<int>(db,"SELECT Id FROM dbo.Expenses WHERE ExpenseNumber=N'EXP-OLD-0001';");
        var oldDate = Scalar<DateTime>(db,"SELECT ExpenseDate FROM dbo.Expenses WHERE Id=@id",("@id",oldId));
        var migrated = Read(new(oldDate,oldDate));
        Check(migrated.Proofs[oldId] is { OK:false, BookCost:null, Allocated:125m } && !migrated.Postings.Any(l => l.SourceId==oldId)
            && Table(Page(migrated,f:new(InvoiceIds:new HashSet<int>{oldId})),"فروق المطابقة").Rows.Single().Display.Contains("توزيع بلا قيد خصم"),
            "An actual Q21 migrated payment is document evidence but not a fabricated cash transaction");

        using var till = new SqlConnection(settings.GetConnectionString()); till.Open();
        Check(Scalar<int>(till,"SELECT HAS_PERMS_BY_NAME('dbo.ReadExpenseReport','OBJECT','EXECUTE')")==1
            && Scalar<int>(till,"SELECT HAS_PERMS_BY_NAME('dbo.Expenses','OBJECT','UPDATE')")==0,"Limited till can execute the report without new expense write rights");
        using (var cmd = new SqlCommand("dbo.ReadExpenseReport",till) {CommandType=CommandType.StoredProcedure})
        {
            cmd.Parameters.AddWithValue("@FromDate",day); cmd.Parameters.AddWithValue("@ThroughDate",day);
            cmd.Parameters.AddWithValue("@PreviousStartUtc",previous.StartUtc); cmd.Parameters.AddWithValue("@UntilUtc",period.EndUtc);
            using var r = cmd.ExecuteReader(); int sets=0;
            do { sets++; while(r.Read()) for(int n=0;n<r.FieldCount;n++) if(r.GetValue(n) is string s && new[]{secret,phone,address}.Any(s.Contains))
                throw new InvalidOperationException("Private expense description, note, account address or counterparty left the read endpoint"); } while(r.NextResult());
            Check(sets==6,"Six staged, privacy-minimised expense report result sets");
        }
        Check(before==Footprint(db),"Rerunning the installer and reporting do not change money, expenses, sequences, payments or audit");

        // Keep the net of the extra pair zero: an apparently matching total must not hide duplicate/wrong-direction postings.
        Execute(db,"""
            INSERT dbo.FinancialTransactions(FinancialAccountId,OccurredAtUtc,IsIncoming,Amount,Category,BalanceAfter,ActorUserId,SourceEntityType,SourceEntityId)
            SELECT @cash,@at,inc,amt,N'مصروف',500,@actor,src,id FROM (VALUES
                (1,7.25,N'Expense',@id),(0,7.25,N'Expense',@id),(1,1.55,N'Expense',2147483600),
                (0,987.65,N'PurchaseInvoice',@id)) x(inc,amt,src,id);
            """,("@cash",cash),("@actor",actor),("@at",next.StartUtc),("@id",split.ExpenseId));
        var damaged = Read(next); var cashReview = Page(damaged,true,new(AccountId:cash));
        Check(damaged.Proofs[split.ExpenseId] is { BookCost:300m, OK:false } && Metric(cashReview,"منصرف المصروفات")==127.25m
            && Metric(cashReview,"وارد مرتبط بمصروف")==8.80m && Metric(cashReview,"صافي الخصم")==118.45m
            && Table(cashReview,"قيود تحتاج مراجعة").Rows.Count==4 && cashReview.Tables[0].Rows.Single(r => r.Display.Contains("قيد بلا مستند")).FinancialSource==null,
            "Orphan/incoming/duplicate Expense postings retain their actual effect without a fake link; unrelated outflow with the same source ID is excluded");
    }
}
