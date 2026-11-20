using System.IO;
using System.Windows;
using System.Windows.Controls;
using ClosedXML.Excel;
using ProjectZ.Models;
using ProjectZ.Services;
using ProjectZ.ViewModels;
using ProjectZ.Views;

internal static class FinanceReportUiTests
{
    private sealed class Source : IFinanceReportSource
    {
        public Func<ReportPeriod,CancellationToken,Task<FinanceReportData>> Read=(p,_)=>Task.FromResult(Sample(p));
        public Task<FinanceReportData> LoadAsync(ReportPeriod p,CancellationToken t)=>Read(p,t);
    }
    private static FinanceReportData Sample(ReportPeriod p)
    {
        var at=p.StartUtc.AddHours(1);var old=p.StartUtc.AddDays(-30);
        var cash=new BookAccount(7,0,"فرع","",true,true,old,at,1085m,0,0,0,0,100m,null,1085m);
        var wallet=cash with {Id=8,Provider=1,MaskedIdentifier="••••0032",Current=1000m,DayOutLimit=1000,PreviousClose=2000m,LatestPostedBalance=1000m,Active=false};
        var fresh=cash with {Id=9,Provider=2,MaskedIdentifier="••••@…",CreatedUtc=at,Current=52.25m,PreviousClose=null,LatestPostedBalance=52.25m};
        var fee=new FeeTransfer(31,8,7,1,1000,10,1010,"••••9876",2,at,true,p.EndUtc,2,null,null,true,true);
        return new(p,at.AddHours(3),new Dictionary<int,BookAccount>{{7,cash},{8,wallet},{9,fresh}},
            [new(1,7,at,true,1010,1110,true,2,FinanceSource.CustomerTransfer,31,true,"","تحويل من العميل",null),
             new(2,8,at,false,1000,1000,true,2,FinanceSource.CustomerTransfer,31,true,"","تحويل للعميل",null),
             new(3,7,at.AddHours(1),false,25,1085,false,3,FinanceSource.Expense,21,true,"EXP-2026-0021","دفع مصروف",null),
             new(4,9,at.AddHours(2),true,2.25m,52.25m,false,2,FinanceSource.Independent,null,false,"","تعديل إيجابي",null)],
            new Dictionary<int,FeeTransfer>{{31,fee}},[new(8,false,1000,1000)],new Dictionary<int,string>{{2,"أحمد"},{3,"أحمد"}});
    }
    internal static void Run(Action<bool,string> check,Action pump)
    {
        var src=new Source();FinanceLink? opened=null;
        var vm=new FinanceReportsViewModel(src,l=>opened=l);var view=new DocumentReportView{DataContext=vm};
        vm.RefreshAsync().GetAwaiter().GetResult();view.Measure(new Size(980,620));view.Arrange(new Rect(0,0,980,620));view.UpdateLayout();pump();
        void Mode(FinanceReportMode m)=>vm.Mode=vm.Modes.Single(c=>c.Id==(int)m);
        void Table(string title)=>vm.SelectedTable=vm.Page!.Tables.Single(t=>t.Title==title);
        var grid=(DataGrid)view.FindName("Results");
        check(vm.IsMovement && grid.Items.Count==4 && grid.ActualHeight>80 && grid.Columns.Count==vm.SelectedTable!.Columns.Length && vm.Page!.Metrics[2].Value==-12.75m,
            "Finance starts on the actual ledger, not an earnings or current balance summary");
        Table("حركة الحسابات");vm.SelectedRow=vm.SelectedTable!.Rows.Single(r=>r.Display[0].Contains("#7"));vm.DrillCommand.Execute(null);
        check(vm.HasDrill && vm.SelectedTable!.Rows.Count==2 && vm.Page!.Metrics[2].Value==985m,"Account drill uses exact leg keys, not both sides of a transfer");
        vm.BackToResultsCommand.Execute(null);check(vm.SelectedTable!.Title=="حركة الحسابات","Finance back restores the grouped table");
        vm.Issuer=vm.Issuers.Single(c=>c.Id==3);vm.Direction=vm.Directions.Single(c=>c.Id==0);Table("قيود الفترة");vm.SelectedRow=vm.SelectedTable!.Rows.Single();
        vm.DrillCommand.Execute(null);check(opened==new FinanceLink(FinanceDestination.Expense,21),"Filtered expense opens by its own durable document key");
        vm.OpenAccountCommand.Execute(null);check(opened==new FinanceLink(FinanceDestination.Account,7),"Account button uses the paying account rather than the expense id");
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xlsx");
        try
        {
            ReportWorkbook.Save(path,"متجر",vm.Page!);using(var wb=new XLWorkbook(path))
            {
            var sh=wb.Worksheet("قيود الفترة");
            check(sh.RightToLeft && sh.LastRowUsed()!.RowNumber()==2 && sh.Cell(2,2).DataType==XLDataType.DateTime && sh.Cell(2,10).DataType==XLDataType.Number && sh.Cell(2,10).GetDouble()==25
                && wb.Worksheet("الملخص").Cell(3,1).GetString()==vm.Page!.Context,"Finance Excel round trip preserves filtered rows, numeric cash, Egypt dates and scope");
            }
            var paper=ReportPaper.Build("متجر",vm.Page!,vm.SelectedTable!);
            var head=paper.Pages[0].GetPageRoot(false)!.Children.OfType<StackPanel>().Single().Children.OfType<TextBlock>().Select(t=>t.Text).ToArray();
            check(paper.Pages.Count>=2 && head.Contains(vm.Page!.Context) && head.Any(t=>t.Contains("تقارير المالية")) && head.Any(t=>t.Contains("25.00")),"A4 has all column bands, finance totals and the exact filtered context");
            Mode(FinanceReportMode.FeeEvents);vm.Search="9876";
            check(vm.Page!.Metrics[2].Value==10 && vm.Issuer.Id==null && vm.Direction.Id==null && !vm.AccountChoices.Any(a=>a.Id==7),"Yesterday's posted fee survives reversal, no cash account offered as a wallet");
            ReportWorkbook.Save(path,"متجر",vm.Page);using var fees=new XLWorkbook(path);
            check(fees.Worksheet("دفتر العمولات").Cell(2,5).GetString()=="••••9876" && !vm.Page.Context.Contains("9876"),"Masked contact only in the fee workbook; never echo arbitrary typed identifiers to output");
            Mode(FinanceReportMode.IssuedTransfers);check(vm.Page!.Metrics[2].Value==0 && vm.SelectedTable!.Rows.Single().Display.Contains("معكوسة"),"Issuance cohort reports current fee separately, even though dated accrual was +10");
        }
        finally {if(File.Exists(path))File.Delete(path);}
        Mode(FinanceReportMode.Statement);vm.SelectedAccount=vm.Accounts.Single(a=>a.Id==9);
        check(vm.Page!.Metrics[0].Value==0 && vm.Page.Metrics[1].Value==50 && vm.Page.Metrics[2].Value==2.25m && vm.Page.Metrics[4].Value==52.25m,"A new account starts at zero for the period; capital is separate from posted inflow");
        vm.SelectedAccount=vm.Accounts.Single(a=>a.Id==8);vm.SelectedRow=vm.SelectedTable!.Rows.Single();
        check(!vm.CanOpenAccount,"Stopped wallet cannot open an unrelated active one");vm.DrillCommand.Execute(null);
        check(vm.IsMovement && vm.SelectedAccount.Id==8 && vm.SelectedTable!.Rows.Count==1 && vm.Page!.Metrics[1].Value==1000,"Historical account statements can still drill into a stopped wallet's filtered ledger");
        Mode(FinanceReportMode.Limits);Table("استخدام الفترة باليوم");check(vm.SelectedTable!.Rows.Count==1 && vm.SelectedTable.Rows[0].Cells.Last() is decimal n && n==1000,"Limit usage excludes cash and legs not flagged for limits");
        vm.From=vm.Through!.Value.AddDays(1);vm.RefreshAsync().GetAwaiter().GetResult();check(vm.Page==null && !vm.CanPrint && !vm.CanExport,"Invalid finance dates clear old export/print data");
        vm.From=vm.Through;src.Read=(_,_)=>throw new InvalidOperationException("اختبار SQL");vm.RefreshAsync().GetAwaiter().GetResult();check(!vm.CanExport && vm.Status.Contains("اختبار SQL"),"Source failures cannot masquerade as empty books");
        var pending=new TaskCompletionSource<FinanceReportData>();src.Read=(_,_)=>pending.Task;var task=vm.RefreshAsync();vm.Leave();pending.SetResult(Sample(new(vm.From,vm.Through)));pump();check(task.IsCompleted && vm.Page==null,"Late read cannot resurrect a finance snapshot after leaving");
        // The actual operational destinations clear conflicting filters without posting anything.
        var main=new MainViewModel(new UserSession(0,"t","اختبار"),()=>{},financeReportSource:src);
        var real=main.VodafoneRecords.FilteredOperations.First(o=>o.IsCustomerTransfer);
        int cashId=main.MyAccounts.CashAccount!.Id;
        src.Read=(p,_)=>{var d=Sample(p);var c=d.Accounts[7] with {Id=cashId};var w=d.Accounts[8] with {Id=real.WalletId,Active=true};
            var t=d.Transfers[31] with {Id=real.Id,WalletId=w.Id,CashId=c.Id};return Task.FromResult(d with {Accounts=new Dictionary<int,BookAccount>{{c.Id,c},{w.Id,w}},Legs=[d.Legs[0] with {AccountId=c.Id,SourceId=t.Id}],Transfers=new Dictionary<int,FeeTransfer>{{t.Id,t}},Usage=[]});};
        var menu=main.Categories.Single(c=>c.Title=="التقارير").Items[3];main.VodafoneRecords.CustomerFilter="غير موجود";main.MyAccounts.WalletSearchText="غير موجود";
        main.SelectItem(menu);main.FinanceReports.SelectedRow=main.FinanceReports.SelectedTable!.Rows.Single();main.FinanceReports.OpenAccountCommand.Execute(null);
        check(main.IsMyAccountsSelected && main.MyAccounts.IsCashSelected && main.MyAccounts.WalletSearchText.Length==0,"Report opens the actual cash register despite prior filters");
        int details=0;main.VodafoneRecords.DetailsRequested=o=>details=o.Id;
        main.SelectItem(menu);main.FinanceReports.SelectedRow=main.FinanceReports.SelectedTable!.Rows.Single();main.FinanceReports.DrillCommand.Execute(null);
        check(main.IsVodafoneRecordsSelected && details==real.Id && main.VodafoneRecords.SelectedOperation?.Id==real.Id,"Opening a transfer reaches the exact provider record, not its wallet/cash ledger ID");
        details=0;main.VodafoneRecords.OpenFromReport(int.MaxValue);check(main.VodafoneRecords.SelectedOperation==null && details==0 && main.VodafoneRecords.IsErrorStatus,"Missing transfer does not open an arbitrary record");
    }
}
