using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClosedXML.Excel;
using ProjectZ.Models;
using ProjectZ.Services;
using ProjectZ.ViewModels;
using ProjectZ.Views;
using static ProjectZ.Models.OverallMeasure;

internal static class OverallReportUiTests
{
    private sealed class Source : IOverallReportSource
    {
        public Func<ReportPeriod,CancellationToken,Task<OverallReportData>> Load=(p,_)=>Task.FromResult(Sample(p));
        public Task<OverallReportData> LoadAsync(ReportPeriod period,CancellationToken token)=>Load(period,token);
    }
    private static OverallReportData Sample(ReportPeriod p)
    {
        var day=DateOnly.FromDateTime(p.From); var prior=day.AddDays(-(p.Through-p.From).Days-1);
        DailyFigure F(OverallMeasure k,decimal a)=>new(day,k,a,0,1);
        return new(p,p.StartUtc.AddHours(8),[F(SaleIssue,1000),F(SalesCost,700),F(SaleReceipts,200),F(ServiceEarned,100),F(ServiceParts,30),
            F(ServiceAdvance,45),F(FeeIssued,10),F(ExpenseWritten,90),F(Damage,100),F(Compensation,80),F(PurchaseCreate,3000),F(SupplierPaid,400),
            F(CashIn,1255),F(CashOut,400),F(VodafoneOut,1000),new(prior,ExpenseWritten,50,0,2)],
            [new(0,1,125),new(1,2,900)],[new("تشغيل",90,30),new("سابق",0,20)]);
    }
    private static IEnumerable<string> PaperText(DependencyObject root)
    {
        if(root is TextBlock t) yield return t.Text;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var text in PaperText(VisualTreeHelper.GetChild(root,i)))yield return text;
    }
    internal static void Run(Action<bool,string> check,Action pump)
    {
        var src=new Source(); var vm=new OverallReportsViewModel(src);
        var when=new DateTime(2026,3,1); vm.From=when; vm.Through=when.AddDays(6);
        var view=new DocumentReportView {DataContext=vm}; vm.RefreshAsync().GetAwaiter().GetResult();
        view.Measure(new Size(980,620)); view.Arrange(new Rect(0,0,980,620)); view.UpdateLayout();pump();
        var grid=(DataGrid)view.FindName("Results"); var page=vm.Page!; var bridge=vm.SelectedTable!;
        check(bridge.Title==OverallReportProjection.Bridge && grid.Items.Count==6 && grid.ActualHeight>80 && view.FlowDirection==FlowDirection.RightToLeft
            && !vm.ShowDocumentTools && !vm.ShowModeButtons && ((Grid)((StackPanel)((TextBox)view.FindName("DocumentSearch")).Parent).Parent).Visibility==Visibility.Collapsed,
            "Default table explains the result; no invoice modes, document searches or identifiers");
        check(page.Metrics.Select(m=>m.Value).SequenceEqual([270m,-50m,320m,-145m]) && page.Metrics[0].Label=="ربح تشغيلي"
            && page.Context.Contains("2026-02-22") && page.Context.Contains("2026-02-28"),
            "300 margin + 70 service + 10 fees - 90 written expense - 20 net damage = 270 profit, not the -145 cash change");
        var result=bridge.Rows.Last();check(Equals(result.Cells[1],270m) && Equals(result.Cells[2],-50m) && Equals(result.Cells[3],320m)
            && bridge.Rows.First().Display.Contains("جديد") && bridge.Rows.First().Cells[4]==null,"Comparison shows signed differences, no percentage on a zero base");
        foreach(var table in page.Tables)
        {
            vm.SelectedTable=table;
            foreach(var row in table.Rows)
            {
                vm.SelectedRow=row;
                if(row.InvoiceIds.Length!=0 || row.CanOpenInvoice || row.EventKeys!=null || row.FinancialSource!=null || row.CustomerId!=null || row.SupplierId!=null || row.PartId!=null || row.FinancialAccount!=null || vm.DrillCommand.CanExecute(null))
                    throw new InvalidOperationException("Unexpected document target in executive report: "+table.Title);
            }
        }
        check(page.Tables.Single(t=>t.Title==OverallReportProjection.Daily).Rows.Count==7 && page.Tables.Single(t=>t.Title=="تصنيفات المصروفات").Rows.Last().Display.Contains("سابق"),
            "Daily and category summaries keep previous-only activity without revealing expense or account records");
        var p=new ReportPeriod(when,when.AddDays(6)); var sample=Sample(p);
        var loss=OverallReportProjection.Build(sample with {Figures=[new(DateOnly.FromDateTime(when),ExpenseWritten,12,0,1)]},"EGP");
        var unknown=OverallReportProjection.Build(sample with {Figures=[..sample.Figures,new(DateOnly.FromDateTime(when),ServiceParts,0,1,1)]},"EGP");
        var even=OverallReportProjection.Build(sample with {Figures=[new(DateOnly.FromDateTime(when),FeeIssued,10,0,1),new(DateOnly.FromDateTime(when),FeeReversed,10,0,1)]},"EGP");
        var empty=OverallReportProjection.Build(sample with {Figures=[]},"EGP");
        check(loss.Metrics[0].Label=="خسارة تشغيلية" && even.Metrics[0].Label=="تعادل تشغيلي" && empty.Metrics[0].Label=="لا نشاط مسجّل" && unknown.Metrics[0].Value==null
            && unknown.Metrics[3].Value==-145 && unknown.Tables.Single(t=>t.Title==OverallReportProjection.Review).Rows.Count==1 && unknown.Warning.Contains("غير مكتملة"),
            "Distinguish a loss, an earned zero, an empty period and an uncertifiable P&L with known liquidity");
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xlsx");
        try
        {
            ReportWorkbook.Save(path,"متجر",unknown); using var book=new XLWorkbook(path);
            var sheet=book.Worksheet(OverallReportProjection.Bridge);var daily=book.Worksheet(OverallReportProjection.Daily);
            check(sheet.RightToLeft && sheet.LastRowUsed()!.RowNumber()==7 && sheet.Cell(2,2).GetDouble()==300 && sheet.Cell(7,2).IsEmpty() && sheet.Cell(7,3).GetDouble()==-50
                && daily.Cell(2,1).DataType==XLDataType.DateTime && daily.Cell(2,5).IsEmpty() && book.Worksheets.Count==unknown.Tables.Count+1
                && book.Worksheet("الملخص").Cell(3,1).GetString()==unknown.Context,"Reopened XLSX contains only aggregate sheets, typed amounts/days, actual prior, and blank unknowns");
            var doc=ReportPaper.Build("متجر",page,bridge);
            var paper=doc.Pages.SelectMany(pg=>PaperText(pg.GetPageRoot(false)!)).ToList();
            check(doc.Pages.Count>=1 && paper.Contains(page.Context) && paper.Contains("270.00") && paper.Contains("-20.00") && !paper.Contains("3,000.00"),
                "A4 prints the chosen profit bridge with the same signed values, not raw rows or other departments");
        }
        finally { if(File.Exists(path)) File.Delete(path); }
        vm.From=when.AddMonths(1); check(vm.Page==null && !vm.CanPrint && !vm.CanExport,"Editing the dates removes the exportable snapshot");
        vm.RefreshAsync().GetAwaiter().GetResult(); check(vm.Page==null && vm.Status.Contains("366"),"Reversed dates are rejected rather than returning old figures");
        vm.From=when; src.Load=(_,_)=>throw new InvalidOperationException("اختبار الانقطاع");vm.RefreshAsync().GetAwaiter().GetResult();
        check(vm.Page==null && vm.Status.Contains("اختبار الانقطاع"),"An unavailable read is not a zero result");
        var pending=new TaskCompletionSource<OverallReportData>(); src.Load=(_,_)=>pending.Task;var wait=vm.RefreshAsync();vm.Leave();pending.SetResult(sample);pump();
        check(wait.IsCompleted && !vm.CanExport && vm.SelectedTable==null,"Navigation cancels an in-flight aggregate snapshot");
    }
}
