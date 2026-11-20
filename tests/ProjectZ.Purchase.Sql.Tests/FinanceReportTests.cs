using Microsoft.Data.SqlClient;
using ProjectZ.Data;
using ProjectZ.Models;
using ProjectZ.Services;
using static SqlTest;
using static ProjectZ.Models.FinanceReportMode;

internal static class FinanceReportTests
{
    internal static void Run(SqlConnection db,ConnectionSettingsProvider settings,int actor,int cash,int legacyId)
    {
        var repo=new FinanceReportRepository(settings);
        FinanceReportData Read(ReportPeriod p)=>repo.LoadAsync(p,CancellationToken.None).GetAwaiter().GetResult();
        ReportPage Page(FinanceReportData d, FinanceReportMode mode,ReportFilter? f=null)=>FinanceReportProjection.Build(d,mode,f??new(),"ج.م");
        decimal? Metric(ReportPage page,string name)=>page.Metrics.Single(m=>m.Label==name).Value;
        var near=new ReportPeriod(ReportPeriod.Today.AddDays(-1),ReportPeriod.Today.AddDays(1));
        // The migration fixture deliberately removes its original expense ledger. Post a new real expense here;
        // the migrated header/payment without ledger evidence must not create another money row.
        var expenses=new ExpenseRepository(settings,actor);
        var expense=expenses.Save(new(expenses.LoadCategories().First().Id,DateTime.Today.AddDays(-12),3.50m,"اختبار التقرير",[new(cash,3.50m)]));
        var actual=Read(near);
        Check(actual.Legs.Single(l=>l.Source==FinanceSource.Expense && l.SourceId==expense.ExpenseId).Delta==-3.50m,
            "An expense posts by ledger date, not the backdated date on its document");
        Check(actual.Legs.Count==Scalar<int>(db,"SELECT COUNT(*) FROM dbo.FinancialTransactions WHERE OccurredAtUtc>=@from AND OccurredAtUtc<@until",("@from",near.StartUtc),("@until",near.EndUtc))
            && actual.Legs.Select(l=>l.Id).Distinct().Count()==actual.Legs.Count,"One reported leg per posting, not multiplied by document lines, receipts or refunds");
        Check(actual.Transfers[legacyId].CurrentFee is null && Page(actual,IssuedTransfers,new(InvoiceIds:new HashSet<int>{legacyId})).Warning.Length>0,
            "An overwritten legacy header cannot establish commissions for the dates of its old terms");
        Check(actual.Transfers.Values.Any(t=>t.CashId.HasValue && t.CashDelta==0 && t.ApplicationConfirmed && t.UndoConfirmed),"Zero cash effect requires only a wallet leg, not a fictional cash leg");
        Check(actual.Transfers.Values.Any(t=>t.CashId.HasValue && !t.Reversed && !t.ApplicationConfirmed),"A saved commission whose account legs disagree stays unconfirmed");
        foreach(var src in new[]{FinanceSource.Sale,FinanceSource.Purchase,FinanceSource.Service,FinanceSource.Expense,FinanceSource.CustomerTransfer})
            Check(actual.Legs.Any(l=>l.Source==src && actual.SourceLink(l)!=null),$"The finance ledger resolves real {src} postings to operational records");
        var finance=new FinancialRepository(settings,actor);
        const string walletSecret="01055557788",customerSecret="01277772211",address="mask-me@instapay",note="PRIVATE-FIN-29";
        int a=finance.SaveAccount(null,FinancialAccountKind.VodafoneCash,walletSecret,walletSecret,2000,1500,3000,3500,6000,note,false).AccountId;
        int b=finance.SaveAccount(null,FinancialAccountKind.VodafoneCash,"فرع", "01055557789",500,0,0,0,0,note,false).AccountId;
        int ip=finance.SaveAccount(null,FinancialAccountKind.InstaPay,"حساب "+address,address,100,15,0,0,0,note,false).AccountId;
        finance.SaveTransaction(ip,FinancialMovementKind.Deposit,15,customerSecret,note,true);
        finance.SaveTransaction(ip,FinancialMovementKind.PositiveAdjustment,6,customerSecret,note,false);
        var now=Read(near);var usage=now.Usage.Single(u=>u.AccountId==ip && u.Incoming);
        Check(usage.Today==15 && usage.Month==15 && Metric(Page(now,Limits,new(AccountId:ip)),"حدود نشطة بلغت الحد")==1,"Wallet limit uses the stored flag and current Egypt window, not every deposit or manual adjustment");
        var first=finance.ExecuteCustomerTransfer(FinancialAccountKind.VodafoneCash,new(a,true,customerSecret,1000,10,note));
        var replacement=finance.UpdateCustomerTransfer(first.TransferId,first.RowVersion,FinancialAccountKind.VodafoneCash,new(b,false,customerSecret,30,2,note));
        finance.SetAccountActive(a,false);
        var day=new DateTime(2041,3,8);var period=new ReportPeriod(day,day);
        Execute(db,"""
            UPDATE dbo.CustomerTransfers SET OccurredAtUtc=@start,ReversedAtUtc=@end WHERE Id=@first;
            UPDATE dbo.CustomerTransfers SET OccurredAtUtc=@end WHERE Id=@second;
            UPDATE dbo.FinancialTransactions SET OccurredAtUtc=CASE WHEN SourceEntityId=@first AND Category<>N'عكس تحويل' THEN @start ELSE @end END
              WHERE SourceEntityType=N'CustomerTransfer' AND SourceEntityId IN (@first,@second);
            """,("@start",period.StartUtc),("@end",period.EndUtc),("@first",first.TransferId),("@second",replacement.TransferId));
        var startState=WalletCorrectionTests.Footprint(db);
        var one=Read(period);var two=Read(new(day.AddDays(1),day.AddDays(1)));
        Check(one.Legs.Count==2 && Metric(Page(one,Ledger),"وارد الحسابات")==1010 && Metric(Page(one,Ledger),"منصرف الحسابات")==1000 && Metric(Page(one,Ledger),"صافي أثر القيود")==10,
            "Egypt start included, next midnight excluded: cash +1010 and wallet -1000, not revenue of 1010");
        Check(Metric(Page(one,FeeEvents),"صافي عمولة الفترة")==10 && Metric(Page(one,IssuedTransfers),"العمولة السارية المثبتة")==0 && one.Transfers[first.TransferId].Replacement==replacement.TransferId,
            "Later correction never overwrites yesterday's 10 earned, while yesterday's issued transfer is now reversed");
        Check(Metric(Page(two,FeeEvents),"صافي عمولة الفترة")==-8 && Metric(Page(two,IssuedTransfers),"العمولة السارية المثبتة")==2
            && two.Transfers[replacement.TransferId].Predecessor==first.TransferId && two.Legs.Count==4,"Correction day shows -10 and +2 exactly once across two wallets, with source lineage");
        var statement=Page(one,Statement,new(AccountId:a));
        Check(Metric(statement,"أول الفترة")==2000 && Metric(statement,"آخر الفترة")==1000 && statement.Tables[0].Rows.Single().FinancialAccount==null && one.Accounts[a].Current==2000,
            "Historical close for a stopped wallet is separate from its current balance; no dead account link");
        Check(one.Transfers[first.TransferId].MaskedCustomer.EndsWith("2211") && one.Accounts[a].MaskedIdentifier.EndsWith("7788") && one.Accounts[ip].SafeAlias!=address,
            "Only partial customer and wallet identifiers are retained");
        using var till=new SqlConnection(settings.GetConnectionString());till.Open();
        Check(Scalar<int>(till,"SELECT HAS_PERMS_BY_NAME('dbo.ReadFinanceReport','OBJECT','EXECUTE')")==1 && Scalar<int>(till,"SELECT HAS_PERMS_BY_NAME('dbo.CustomerTransfers','OBJECT','UPDATE')")==0,"Till has the reporting endpoint without new posting permissions");
        using(var cmd=new SqlCommand("dbo.ReadFinanceReport",till){CommandType=System.Data.CommandType.StoredProcedure})
        {
            cmd.Parameters.AddWithValue("@FromUtc",period.StartUtc);cmd.Parameters.AddWithValue("@UntilUtc",period.EndUtc);
            using var r=cmd.ExecuteReader();int sets=0;
            do {sets++;while(r.Read())for(int n=0;n<r.FieldCount;n++)if(r.GetValue(n) is string text && new[]{customerSecret,walletSecret,address,note}.Any(text.Contains))throw new Exception("Unmasked identifier/private note crossed the report contract");}while(r.NextResult());
            Check(sets==6,"Six redacted, consistent result sets; account aliases cannot forward their raw number/address");
        }
        Check(WalletCorrectionTests.Footprint(db)==startState,"Reading all modes changes no account, version, payment, ledger or audit row");
        // A legacy/orphan posting with no header in this period is an actual book movement, not a zero commission.
        var orphanDay=new ReportPeriod(day.AddDays(2),day.AddDays(2));
        Execute(db,"""
            INSERT dbo.FinancialTransactions(FinancialAccountId,OccurredAtUtc,IsIncoming,Category,Amount,BalanceAfter,ActorUserId,SourceEntityType,SourceEntityId)
              VALUES(@id,@at,1,N'سحب للعميل',12.22,666.66,@user,N'CustomerTransfer',2147483600);
            """,("@id",ip),("@at",orphanDay.StartUtc),("@user",actor));
        var orphan=Read(orphanDay);var fees=Page(orphan,FeeEvents);
        Check(Metric(Page(orphan,Ledger),"وارد الحسابات")==12.22m && orphan.SourceLink(orphan.Legs.Single())==null && Metric(fees,"صافي عمولة الفترة")==null
            && fees.Tables.Single(t=>t.Title=="قيود عمولة تحتاج مطابقة").Rows.Count==1,"An orphan transfer leg in an otherwise empty period cannot silently produce zero earnings or an invented destination");
        var gap=Page(orphan,Statement,new(AccountId:ip));
        Check(gap.Tables.Single(t=>t.Title=="مطابقة الأرصدة").Rows.Count==1 && Metric(gap,"آخر الفترة")==666.66m,
            "Discontinuous posted balances and a live mismatch are reported, not rewritten to the expected arithmetic balance");
    }
}
