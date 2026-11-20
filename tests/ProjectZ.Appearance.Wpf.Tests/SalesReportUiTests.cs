using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ClosedXML.Excel;
using ProjectZ.Models;
using ProjectZ.Services;
using ProjectZ.ViewModels;
using ProjectZ.Views;

internal static class SalesReportUiTests
{
    private sealed class Source : ISalesReportSource
    {
        public Func<SalesReportPeriod,CancellationToken,Task<SalesReportData>> Read = (p,_) => Task.FromResult(Sample(p));
        public Task<SalesReportData> LoadAsync(SalesReportPeriod period,CancellationToken token) => Read(period,token);
    }
    private static SalesReportData Sample(SalesReportPeriod period)
    {
        var time=period.StartUtc.AddHours(3);
        var a=new ReportDocument(31,"INV-R-31",1,"=1+1",51,"أمين");
        var b=new ReportDocument(32,"INV-R-32",2,"=1+1",52,"أمين");
        return new(period,time.AddHours(4),[new(a,time,2,200,20,90,60,100,10,0),new(b,time,0,30,0,30,10,30,0,0)],
            [new(31,1,11,"هاتف","أجهزة","000123","قطعة",false,2,1,100,60,200,180,90,90,60),
             new(32,1,12,"غطاء","ملحقات","001","قطعة",false,1,0,30,10,30,30,0,30,10)],
            [new(a,SalesEventKind.Issue,31,time,51,"أمين",a.Number,"هاتف",2,180,120,null,null,null,0,0),
             new(a,SalesEventKind.Return,9,time.AddHours(1),52,"أمين","RET-9","هاتف",-1,-90,-60,null,null,null,0,0),
             new(a,SalesEventKind.Collection,10,time,51,"أمين","10","نقدي",null,null,null,77,0,"الخزنة",40,0),
             new(a,SalesEventKind.Collection,11,time,51,"أمين","11","محفظة",null,null,null,88,1,"فرع",60,0),
             new(a,SalesEventKind.Refund,12,period.EndUtc,52,"أمين","12","رد في اليوم التالي",null,null,null,88,1,"فرع",0,10),
             new(b,SalesEventKind.Collection,13,time,52,"أمين","13","محفظة أخرى",null,null,null,89,1,"فرع",30,0)]);
    }
    internal static void Run(Action<bool,string> check,Action pump)
    {
        var source=new Source(); int opened=0;
        var vm=new SalesReportsViewModel(source,id=>opened=id);
        var view=new SalesReportsView { DataContext=vm };
        vm.RefreshAsync().GetAwaiter().GetResult(); pump();
        var grid=(DataGrid)view.FindName("Results");
        check(vm.CanExport && vm.Page!.Tables.Count==9 && vm.Page.Metrics[1].Value==120 && grid.Columns.Count==vm.SelectedTable!.Columns.Length,
            "Visible report uses the shared projection and exposes all cost/profit columns");
        check(vm.Page!.Tables.Single(t=>t.Title=="العملاء").Rows.Count==2 && vm.Page.Tables.Single(t=>t.Title=="الكاشير").Rows.Count==2,
            "Equal display names never merge different customer or cashier IDs");
        vm.Detail="000123";
        check(vm.SelectedTable!.Rows.Single().InvoiceIds[0]==31 && vm.Page.Metrics[4].Value==90,"Barcode filter selects the full issued invoice including later refund");
        vm.SelectedTable=vm.Page.Tables.Single(t=>t.Title=="أداء الأصناف"); vm.SelectedRow=vm.SelectedTable.Rows.Single(); vm.DrillCommand.Execute(null);
        check(vm.SelectedTable!.Title=="الفواتير" && vm.Page.Context.Contains("هاتف"),"Group drill-down exposes its underlying invoices and labels the scope");
        vm.SelectedRow=vm.SelectedTable.Rows.Single();vm.DrillCommand.Execute(null);
        check(opened==31,"Detail navigation uses the persisted invoice ID");
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xlsx");
        try
        {
            SalesReportWorkbook.Save(path,"متجر الاختبار",vm.Page);
            using var book=new XLWorkbook(path);
            var invoices=book.Worksheet("الفواتير"); var lines=book.Worksheet("بنود الفواتير");
            check(book.Worksheets.Count==vm.Page.Tables.Count+1 && invoices.LastRowUsed()!.RowNumber()==2 && invoices.RightToLeft
                && invoices.Cell(2,2).DataType==XLDataType.DateTime && invoices.Cell(2,8).GetDouble()==90
                && !invoices.Cell(2,3).HasFormula && invoices.Cell(2,3).GetString().StartsWith("=1+1")
                && lines.Cell(2,4).GetString()=="000123" && book.Worksheet("الملخص").Cell(3,1).GetString()==vm.Page.Context,
                "Real .xlsx round-trip: filtered typed dates and amounts, literal formula-like names, leading zero barcode, context and all sheets");
        }
        finally { if(File.Exists(path))File.Delete(path); }
        var printable=vm.SelectedTable!;
        var many=printable with { Rows=Enumerable.Range(1,75).Select(n=>new SalesReportRow([n],printable.Rows[0].Cells.Select((v,i)=>i==0?$"INV-PAPER-{n}":v).ToArray())).ToList() };
        var paper=SalesReportPaper.Build("متجر الورق",vm.Page,many);
        var lastPage=paper.Pages[^1].GetPageRoot(false)!;
        var body=lastPage.Children.OfType<StackPanel>().Single();
        check(paper.Pages.Count>3 && Math.Abs(lastPage.Width-SalesReportPaper.Width)<.1 && body.Children.OfType<Grid>().Last().Children.OfType<Border>().Any(b=>(b.Child as TextBlock)?.Text=="INV-PAPER-75"),
            "A4 preview paginates rows AND columns, repeats the invoice key and includes the final off-screen row");
        var window=new SalesReportPrintWindow(paper); window.Close();
        vm.ClearCommand.Execute(null); vm.SelectedAccount=vm.Accounts.Single(a=>a.Id==88);
        check(vm.Page!.Metrics[1].Value==90 && vm.Page.Metrics[4].Value==90,"Invoice account filter selects the whole invoice, not a pro-rata assignment to a wallet");
        vm.IsMovement=true;
        check(vm.Page.Tables[0].Rows.Count==1 && vm.Page.Metrics[3].Value==60 && vm.Page.Metrics[4].Value==0,
            "Event account filter includes only actual account 88 in range, excluding tomorrow's refund and same-name account 89");
        vm.ClearCommand.Execute(null); vm.SelectedTable=vm.Page!.Tables.Single(t=>t.Title=="حسابات السداد");
        vm.SelectedRow=vm.SelectedTable.Rows.Single(r=>r.Display[0].Contains("#88")); vm.DrillCommand.Execute(null);
        check(vm.SelectedTable!.Title=="دفتر الحركات" && vm.SelectedTable.Rows.Count==1 && vm.Page.Metrics[3].Value==60,
            "Account drill-down in movement mode keeps exactly its contributing events, not other legs on the same invoice");
        vm.ClearCommand.Execute(null);vm.SelectedKind=vm.KindOptions.Single(x=>x.Id==(int)SalesEventKind.Return);
        check(vm.Page.Metrics[0].Value==-90 && vm.Page.Metrics[5].Value==0,"Filtering a goods return never fabricates a cash payout");
        vm.From=vm.Through!.Value.AddDays(1);vm.RefreshAsync().GetAwaiter().GetResult();
        check(vm.Page is null && !vm.CanPrint && !vm.CanExport && vm.Status.Contains("تاريخ"),"Invalid range clears the previous printable/exportable snapshot");
        vm.From=vm.Through; source.Read=(_,_)=>throw new InvalidOperationException("تعذر الاتصال");vm.RefreshAsync().GetAwaiter().GetResult();
        check(!vm.CanExport && vm.Status.Contains("تعذر الاتصال"),"SQL failure is not reported as a successful empty report");
        var tcs=new TaskCompletionSource<SalesReportData>();source.Read=(_,_)=>tcs.Task;
        var pending=vm.RefreshAsync(); vm.From=vm.Through!.Value.AddDays(-2); tcs.SetResult(Sample(new(vm.Through,vm.Through))); pump();
        check(pending.IsCompleted && vm.Page is null && !vm.IsBusy,"A late query after a date change cannot replace the current report");
        var apr=new DateTime(2026,4,24);var dst=new SalesReportPeriod(apr,apr);
        check(dst.StartUtc==new SalesReportPeriod(apr.AddDays(-1),apr.AddDays(-1)).EndUtc && SalesReportPeriod.Local(dst.StartUtc).Date==apr
            && SalesReportPeriod.Local(dst.StartUtc.AddTicks(-1)).Date==apr.AddDays(-1),"Egypt midnight/DST boundary is continuous and workstation independent");
        // Navigate through the actual menu and hosted invoices VM with pre-existing filters.
        var main=new MainViewModel(new UserSession(0,"r","اختبار التقرير"),()=>{},salesReportSource:source);
        var target=main.Invoices.FilteredInvoices.Last(); var data=Sample(new(SalesReportPeriod.Today,SalesReportPeriod.Today));
        source.Read=(_,_)=>Task.FromResult(data with {Invoices=[data.Invoices[0] with {Doc=data.Invoices[0].Doc with {Id=target.Id,Number=target.InvoiceNumber}}]});
        main.Invoices.SearchText="غير موجود";main.SelectItem(main.Categories.Single(c=>c.Title=="التقارير").Items[0]);
        main.SalesReports.SelectedRow=main.SalesReports.SelectedTable!.Rows.Single(); main.SalesReports.DrillCommand.Execute(null);
        check(main.IsInvoicesSelected && main.Invoices.SelectedInvoice?.Id==target.Id && main.Invoices.FilteredInvoices.Contains(target),"Opening an invoice from reports clears conflicting invoice filters and selects the correct record");
    }
}
