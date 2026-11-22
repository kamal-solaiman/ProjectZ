using System.IO;
using System.Windows;
using System.Windows.Controls;
using ClosedXML.Excel;
using ProjectZ.Models;
using ProjectZ.Services;
using ProjectZ.ViewModels;
using ProjectZ.Views;
using static ProjectZ.Models.PurchaseFactKind;

internal static class PurchaseReportUiTests
{
    private sealed class Source : IPurchaseReportSource
    {
        public Func<ReportPeriod,CancellationToken,Task<PurchaseReportData>> Read = (p,_)=>Task.FromResult(Sample(p));
        public Task<PurchaseReportData> LoadAsync(ReportPeriod p,CancellationToken token)=>Read(p,token);
    }
    private static PurchaseReportData Sample(ReportPeriod period)
    {
        var a = new PurchaseReportDocument(31,"PUR-31","00018",51,"مورد",61,"مسجّل",true,true);
        var b = new PurchaseReportDocument(32,"PUR-32","00042",52,"مورد",62,"مسجّل",true,true);
        var c = a with {Id=33,Number="PUR-33",SupplierReference="قديم"};
        var at=period.StartUtc.AddHours(4);
        return new(period,at.AddMinutes(5),new Dictionary<int,PurchaseReportDocument>{{31,a},{32,b},{33,c}},
            [new(31,at,at,1,1000,100,1000,900,350,0,0,50,0,500,false),new(32,at,at,2,25,0,25,25,0,0,0,0,0,25,false)],
            [new(31,1,101,"هاتف","أجهزة","000123","قطعة",1,0,1,600,600,60),new(31,2,102,"شاحن","ملحقات","000456","قطعة",2,0,2,200,400,40),
             new(32,1,103,"كابل","ملحقات","000789","قطعة",1,0,1,25,25,0)],
            [new(Create,1,1,31,null,at,61,"مسجّل","قيد 1","",900,null,null,null,null,null,null,null,false),
             new(Payment,2,0,31,null,at,61,"مسجّل","2","سداد",null,null,300,7,0,"الخزنة",null,null,false),
             new(Payment,3,0,31,null,at,62,"مسجّل","3","سداد",null,null,50,8,1,"المحفظة",null,null,false),
             new(CreditApplication,4,0,31,33,at,62,"مسجّل","PRT-20","مقاصة",null,null,50,null,null,null,77,null,false),
             new(Receipt,5,0,33,null,period.EndUtc,62,"مسجّل","R-10","قبض",null,null,10,8,1,"المحفظة",77,9,false)],[],[]);
    }
    internal static void Run(Action<bool,string> check,Action pump)
    {
        var source=new Source(); int opened=0, supplier=0;
        var vm=new PurchaseReportsViewModel(source,id=>opened=id,id=>supplier=id);
        var view=new DocumentReportView {DataContext=vm};
        vm.RefreshAsync().GetAwaiter().GetResult(); view.Measure(new Size(980,620)); view.Arrange(new Rect(0,0,980,620)); view.UpdateLayout(); pump();
        var grid=(DataGrid)view.FindName("Results");
        check(vm.SectionTitle=="تقارير المشتريات" && grid.Items.Count==2 && grid.ActualHeight>80 && grid.Columns.Count==vm.SelectedTable!.Columns.Length,
            "Purchase workspace binds a visible invoice grid with purchase-specific headings");
        check(vm.Page!.Tables.Single(t=>t.Title=="الموردون").Rows.Count==2 && vm.Suppliers.Count==3,
            "Two suppliers with the same display name have different filters and analysis rows");
        vm.Supplier=vm.Suppliers.Single(x=>x.Id==51); vm.Search="00018"; vm.Detail="000123";
        check(vm.Page.Metrics[1].Value==900 && vm.Page.Tables.Single(t=>t.Title=="بنود الفواتير").Rows.Count==2,
            "Matching a barcode selects the whole invoice, including the other line and its allocated discount");
        vm.SelectedTable=vm.Page.Tables.Single(t=>t.Title=="الموردون"); vm.SelectedRow=vm.SelectedTable.Rows.Single(); vm.OpenSupplierCommand.Execute(null);
        vm.DrillCommand.Execute(null); vm.SelectedRow=vm.SelectedTable!.Rows.Single(); vm.DrillCommand.Execute(null);
        check(opened==31 && supplier==51 && vm.HasDrill && vm.SelectedTable.Title=="الفواتير","Supplier and original invoice drill-down use persisted IDs");
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xlsx");
        try
        {
            ReportWorkbook.Save(path,"متجر الاختبار",vm.Page);
            using var book=new XLWorkbook(path); var lines=book.Worksheet("بنود الفواتير");
            check(book.Worksheets.Count==vm.Page.Tables.Count+1 && book.Worksheet("الملخص").Cell(1,2).GetString()==vm.SectionTitle
                && lines.RightToLeft && lines.LastRowUsed()!.RowNumber()==3 && lines.Cell(2,5).GetString()=="000123"
                && lines.Cell(2,14).DataType==XLDataType.Number && lines.Cell(2,14).GetDouble()==540 && lines.Cell(3,14).GetDouble()==360
                && book.Worksheet("الفواتير").Cell(2,2).DataType==XLDataType.DateTime,"Purchase Excel round-trip retains filtered lines, context, barcode and sortable values");
        }
        finally { if(File.Exists(path))File.Delete(path); }
        var paper=ReportPaper.Build("المحل",vm.Page,vm.SelectedTable);
        var header=paper.Pages[0].GetPageRoot(false)!.Children.OfType<StackPanel>().Single().Children.OfType<TextBlock>().Select(t=>t.Text).ToArray();
        check(paper.Pages.Count>1 && header.Any(s=>s.Contains(vm.SectionTitle)) && header.Contains(vm.Page.Context) && !header.Any(s=>s.Contains("المبيعات")),
            "A4 purchase preview paginates wide tables, prints the actual scope and never uses the sales heading");
        vm.BackToResultsCommand.Execute(null); check(vm.SelectedTable!.Title=="الموردون" && vm.Supplier.Id==51,"Group back preserves supplier and current filters");
        vm.ClearCommand.Execute(null); vm.IsMovement=true; vm.SelectedKind=vm.KindOptions.Single(x=>x.Id==(int)CreditApplication);
        vm.SelectedRow=vm.SelectedTable!.Rows.Single(); vm.OpenRelatedCommand.Execute(null);
        check(vm.HasRelated && opened==33 && vm.Page.Metrics[5].Value==50 && vm.Page.Metrics[1].Value==0 && vm.Page.Metrics[2].Value==0,
            "Application opens the source of credit but does not book incoming or outgoing money");
        vm.ClearCommand.Execute(null); vm.SelectedTable=vm.Page.Tables.Single(t=>t.Title=="حسابات الدفع والقبض");
        vm.SelectedRow=vm.SelectedTable.Rows.Single(r=>r.Display[0].Contains("#8")); vm.DrillCommand.Execute(null);
        check(vm.Page.Metrics[1].Value==50 && vm.Page.Metrics[2].Value==0 && vm.SelectedTable!.Rows.Count==1,
            "Account drill-down keeps only its actual payment leg, not the other wallet or tomorrow's receipt");
        vm.From=vm.Through!.Value.AddDays(1); vm.RefreshAsync().GetAwaiter().GetResult();
        check(vm.Page is null && !vm.CanExport && !vm.CanPrint,"Bad purchase dates invalidate the previous output");
        vm.From=vm.Through; vm.ClearCommand.Execute(null); source.Read=(p,_)=> {
            var d=Sample(p); var gone=d.Documents[31] with {CanOpen=false, CanOpenSupplier=false};
            return Task.FromResult(d with {Documents=new Dictionary<int,PurchaseReportDocument>{{31,gone}},Invoices=[],Lines=[],
                Events=[d.Events[0] with {Kind=Delete,DocumentDelta=null,NeedsReview=true}]}); };
        vm.RefreshAsync().GetAwaiter().GetResult(); vm.SelectedRow=vm.SelectedTable!.Rows.Single();
        check(vm.Page!.Metrics[0].Value==null && vm.Page.Warning.Length>0 && vm.SelectedRow.Display[0].Contains("محذوف")
            && !vm.DrillCommand.CanExecute(null) && !vm.OpenSupplierCommand.CanExecute(null),"Deleted audit and archived supplier remain readable without a fabricated value or destination");
        // Use the real destinations, with filters that would otherwise hide the requested document and supplier.
        var main=new MainViewModel(new UserSession(0,"p","تقرير شراء"),()=>{},purchaseReportSource:source);
        var invoice=main.PurchaseInvoices.FilteredInvoices.Last(); var owner=main.Suppliers.FilteredSuppliers.Single(s=>s.Supplier.Id==invoice.SupplierId);
        source.Read=(p,_)=> {
            var d=Sample(p); var doc=d.Documents[31] with {Id=invoice.Id,Number=invoice.InvoiceNumber,SupplierId=owner.Supplier.Id};
            return Task.FromResult(d with {Documents=new Dictionary<int,PurchaseReportDocument>{{doc.Id,doc}},Invoices=[d.Invoices[0] with {Id=doc.Id}],Lines=[],Notes=[],Events=[],ReturnLines=[]}); };
        var item=main.Categories.Single(c=>c.Title=="التقارير").Items[1]; main.PurchaseInvoices.SearchText="غير موجود"; main.Suppliers.SearchText="غير موجود";
        int details=0; main.PurchaseInvoices.OpenDetailsDialogRequested=d=>details=d.Invoice.Id;
        main.SelectItem(item); main.PurchaseReports.SelectedRow=main.PurchaseReports.SelectedTable!.Rows.Single(); main.PurchaseReports.DrillCommand.Execute(null);
        check(main.IsPurchaseInvoicesSelected && details==invoice.Id && main.PurchaseInvoices.FilteredInvoices.Contains(invoice),"Report opens the existing purchase details on the correct visible invoice");
        main.SelectItem(item); main.PurchaseReports.SelectedRow=main.PurchaseReports.SelectedTable!.Rows.Single(); main.PurchaseReports.OpenSupplierCommand.Execute(null);
        check(main.IsSuppliersSelected && main.Suppliers.SelectedSupplierEntry?.Supplier.Id==owner.Supplier.Id && main.Suppliers.IsInvoicesTabSelected,"Report opens the selected supplier's purchase tab, not spare parts");
        details=0; main.PurchaseInvoices.OpenFromReport(int.MaxValue);
        check(details==0 && main.PurchaseInvoices.SelectedInvoice is null && main.PurchaseInvoices.IsErrorStatus,
            "An invoice removed since the read never opens a different document");
        check(!main.Suppliers.SelectSupplierFromExternal(int.MaxValue,fromReport:true) && main.Suppliers.SelectedSupplierEntry is null && main.Suppliers.IsErrorStatus,
            "An unavailable supplier does not silently select the first profile");
    }
}
