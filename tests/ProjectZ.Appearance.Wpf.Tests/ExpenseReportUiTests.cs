using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClosedXML.Excel;
using ProjectZ.Data;
using ProjectZ.Models;
using ProjectZ.Services;
using ProjectZ.ViewModels;
using ProjectZ.Views;

internal static class ExpenseReportUiTests
{
    private sealed class Source : IExpenseReportSource
    {
        public Func<ReportPeriod,CancellationToken,Task<ExpenseReportData>> Load = (p,_) => Task.FromResult(Sample(p));
        public Task<ExpenseReportData> LoadAsync(ReportPeriod period, CancellationToken token) => Load(period,token);
    }
    private sealed class Register : IExpenseInvoiceReader
    {
        public List<ExpenseInvoiceRecord> Rows = [];
        public List<ExpenseInvoiceRecord> LoadInvoices(DateTime? fromDate = null, DateTime? toDate = null) => Rows;
    }
    private static ExpenseReportData Sample(ReportPeriod p, int cash = 7)
    {
        var old = ExpenseReportData.PreviousOf(p); var day = DateOnly.FromDateTime(p.From);
        var when = p.StartUtc.AddHours(1); var prior = old.EndUtc.AddMinutes(-1);
        return new(p,when,
            [new(41,"EXP-2025-0041",day,300,1,"كهرباء",2,when),new(42,"EXP-2025-0042",day,13.25m,2,"إيجار",3,when),
             new(40,"EXP-2025-0040",day.AddDays(-1),100,1,"كهرباء",2,prior),new(39,"EXP-2025-0039",day.AddDays(-1),50,3,"سابق",2,prior)],
            [new(11,41,cash,120),new(12,41,8,180),new(13,42,8,13.25m),new(10,40,cash,100),new(9,39,8,50)],
            [new(11,41,cash,when,false,120,980,2),new(12,41,8,when,false,180,320,2),
             new(10,40,cash,prior,false,100,1100,2),new(9,39,8,prior,false,50,500,2)],
            [new(cash,0,"فرع",true),new(8,1,"فرع",false)],new Dictionary<int,string>{{2,"مسجّل"},{3,"مسجّل"}});
    }
    private static IEnumerable<string> PaperText(DependencyObject root)
    {
        if (root is TextBlock t) yield return t.Text;
        for (int i=0; i<VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var line in PaperText(VisualTreeHelper.GetChild(root,i))) yield return line;
    }

    internal static void Run(Action<bool,string> check, Action pump)
    {
        var src = new Source(); FinanceLink? opened = null;
        var vm = new ExpenseReportsViewModel(src,link => opened=link);
        var view = new DocumentReportView {DataContext=vm};
        vm.RefreshAsync().GetAwaiter().GetResult();
        view.Measure(new Size(980,620)); view.Arrange(new Rect(0,0,980,620)); view.UpdateLayout(); pump();
        var grid=(DataGrid)view.FindName("Results");
        decimal? Metric(string name) => vm.Page!.Metrics.Single(m => m.Label==name).Value;
        void Table(string title) => vm.SelectedTable=vm.Page!.Tables.Single(t => t.Title==title);
        check(!vm.IsMovement && view.FlowDirection==FlowDirection.RightToLeft && grid.Items.Count==2 && grid.Columns.Count==10 && grid.ActualHeight>80
            && Metric("إجمالي المستندات")==313.25m && Metric("مستندات للمراجعة")==1,"Expense workspace opens on two actual documents without doubling split allocations");
        Table(ExpenseReportProjection.CompareCategory); vm.SelectedRow=vm.SelectedTable!.Rows.Single(r => r.Display.Contains("لا نشاط حالي")); vm.DrillCommand.Execute(null);
        check(vm.HasDrill && vm.SelectedTable!.Title=="أدلة المقارنة" && vm.SelectedTable.Rows.Single().Display.Contains("السابقة") && Metric("مبلغ المجموعة للفترة")==0
            && Metric("مبلغ المجموعة سابقًا")==50 && Metric("التغير")==-100,"Prior-only category opens its real previous evidence, not an empty current group");
        vm.BackToResultsCommand.Execute(null); check(vm.SelectedTable!.Title==ExpenseReportProjection.CompareCategory,"Back restores the originating comparison");
        Table(ExpenseReportProjection.CompareAccount); vm.SelectedRow=vm.SelectedTable!.Rows.Single(r => r.Display[0].Contains("#7")); vm.DrillCommand.Execute(null);
        check(vm.SelectedTable!.Rows.Count==2 && vm.SelectedTable.Rows.All(r => r.Display.Contains("حصة موزّعة")) && Metric("مبلغ المجموعة للفترة")==120
            && Metric("مبلغ المجموعة سابقًا")==100 && Metric("الفرق")==20,"A cash comparison drills to exact current/previous share keys, not both shares of the same invoice");
        vm.BackToResultsCommand.Execute(null);
        vm.Verification=vm.VerificationChoices.Single(o => o.Id==0); Table("فروق المطابقة"); vm.SelectedRow=vm.SelectedTable!.Rows.Single();
        check(vm.SelectedRow.Display.Contains("توزيع بلا قيد خصم") && vm.SelectedRow.Cells[5]==null && !vm.CanOpenAccount,"Missing historical debit is blank, not zero, and a stopped account has no operational link");
        vm.DrillCommand.Execute(null); check(opened==new FinanceLink(FinanceDestination.Expense,42),"Review opens the exact saved expense, not the database ID of the payment line");
        vm.Mode=vm.Modes[1]; vm.SelectedAccount=vm.Accounts.Single(a => a.Id==7); vm.Issuer=vm.Issuers.Single(a => a.Id==2);
        vm.Category=vm.Categories.Single(c => c.Id==1); vm.Search="0041"; pump();
        check(vm.Verification.Id==null && !vm.HasDrill && vm.SelectedTable!.Rows.Count==1 && Metric("صافي الخصم")==120 && !vm.Page!.Context.Contains("0041"),
            "Cash filter in posting mode keeps only 120, labels the actual actor/category, never echoes private searches");
        vm.SelectedRow=vm.SelectedTable!.Rows.Single(); vm.OpenAccountCommand.Execute(null); check(opened==new FinanceLink(FinanceDestination.Account,7),"Open paying account is independent of the invoice target");
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xlsx");
        try
        {
            ReportWorkbook.Save(path,"متجر",vm.Page!); using var workbook=new XLWorkbook(path);
            var sheet=workbook.Worksheet("قيود الخصم والوارد");
            check(sheet.RightToLeft && sheet.LastRowUsed()!.RowNumber()==2 && sheet.Cell(2,2).DataType==XLDataType.DateTime && sheet.Cell(2,8).DataType==XLDataType.Number
                && sheet.Cell(2,8).GetDouble()==120 && workbook.Worksheet("الملخص").Cell(3,1).GetString()==vm.Page!.Context
                && workbook.Worksheets.Count==vm.Page.Tables.Count+1,"Expense workbook reopens with typed, filtered ledger and the same comparison scope");
            var paper=ReportPaper.Build("متجر",vm.Page!,vm.SelectedTable!);
            var printable=paper.Pages.SelectMany(p => PaperText(p.GetPageRoot(false)!)).ToArray();
            check(paper.Pages.Count>=2 && printable.Contains(vm.Page!.Context) && printable.Contains("EXP-2025-0041") && printable.Contains("120.00")
                && !printable.Contains("180.00"),"A4 prints every column band of the selected filtered expense table only");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        Table("حسب الحساب"); vm.SelectedRow=vm.SelectedTable!.Rows.Single(); vm.DrillCommand.Execute(null);
        check(vm.SelectedTable!.Rows.Count==1 && Metric("مبلغ المجموعة للفترة")==120 && !vm.Page!.Tables.Any(t => t.Title==ExpenseReportProjection.CompareCategory),
            "A dated group opens only its posted leg, with no invented comparator");
        vm.BackToResultsCommand.Execute(null); vm.ClearCommand.Execute(null); check(vm.Category.Id==null && vm.SelectedAccount.Id==null && Metric("صافي الخصم")==300,"Clear returns to all dated expense postings");
        vm.Detail="PRIVATE-PAYROLL"; check(vm.SelectedTable!.Rows.Count==0 && !vm.CanExport && !vm.Page!.Context.Contains(vm.Detail),"A private description is not searchable or echoed in an empty report");
        vm.From=vm.Through!.Value.AddDays(1); vm.RefreshAsync().GetAwaiter().GetResult(); check(vm.Page==null && !vm.CanPrint && !vm.CanExport,"Invalid period cannot leave a printable expense snapshot");
        vm.From=vm.Through; src.Load=(_,_)=>throw new InvalidOperationException("اختبار القراءة"); vm.RefreshAsync().GetAwaiter().GetResult();
        check(vm.Status.Contains("اختبار القراءة") && vm.Page==null,"A failed read is not a zero expense report");
        var pending=new TaskCompletionSource<ExpenseReportData>(); src.Load=(_,_)=>pending.Task; var wait=vm.RefreshAsync(); vm.Leave(); pending.SetResult(Sample(new(vm.From,vm.Through))); pump();
        check(wait.IsCompleted && !vm.CanExport,"A late read cannot publish a report after navigation");

        // Enter via a category / an invoice; only transfer the key and dates, then execute one fresh SQL-shaped read.
        int reads=0; var main=new MainViewModel(new UserSession(0,"t","اختبار"),()=>{},expenseReportSource:src);
        var cash=main.MyAccounts.CashAccount!.Id; src.Load=(p,_)=>{reads++; return Task.FromResult(Sample(p,cash));};
        var original=new ExpenseInvoiceRecord(41,"EXP-2025-0041",new DateTime(2025,2,24),300,"PRIVATE-PAYROLL",1,"كهرباء","مسجّل",DateTime.UtcNow,
            [new(cash,"فرع",120),new(8,"فرع",180)]);
        var record=new ExpenseInvoiceListItemViewModel(original);
        main.ExpenseInvoices.AnalyzeCategoryCommand.Execute(record);
        check(reads==1 && main.IsExpenseReportsSelected && main.ExpenseReports.Category.Id==1 && main.ExpenseReports.From==new DateTime(2025,2,1)
            && main.ExpenseReports.Through==new DateTime(2025,2,28) && main.ExpenseReports.SelectedTable!.Rows.Single().FinancialSource?.Id==41,
            "Invoice category badge opens the month's actual category report without forwarding its private description");
        main.ExpenseReports.Mode=main.ExpenseReports.Modes[1]; main.ExpenseReports.SelectedAccount=main.ExpenseReports.Accounts.Single(a => a.Id==cash);
        main.ExpenseReports.SelectedRow=main.ExpenseReports.SelectedTable!.Rows.Single(); main.ExpenseReports.OpenAccountCommand.Execute(null);
        check(main.IsMyAccountsSelected && main.MyAccounts.SelectedAccount?.Id==cash && main.ExpenseReports.Page==null,"Posting opens the operational cash account and clears the hidden snapshot");
        main.ExpenseCategories.AnalyzeCategoryCommand.Execute(main.ExpenseCategories.FilteredCategories.First());
        check(reads==2 && main.IsExpenseReportsSelected && !main.ExpenseReports.IsMovement && main.ExpenseReports.Through==ReportPeriod.Today
            && main.ExpenseReports.SelectedAccount.Id==null,"Category list opens month-to-date in document mode, not a stale payment/account filter");
        var register=new Register {Rows=[original,original with {Id=111,ExpenseNumber="EXP-2025-0111",Description="OTHER"}]};
        var invoices=new ExpenseInvoicesViewModel(register); invoices.RefreshFromDatabase(); invoices.SelectedCategoryFilter="غير موجود";
        ExpenseInvoiceDetailsViewModel? details=null; invoices.OpenDetailsDialogRequested=i=>details=i;
        // Same operational destination used by the shell; only the original invoice contains free text.
        invoices.OpenFromReport(41);
        check(details?.Invoice.Id==41 && details.DescriptionDisplay=="PRIVATE-PAYROLL" && details.Payments.Count==2 && invoices.FilteredInvoices.Single().Id==41,
            "Drilling reaches the original private description and payment list despite a stale register filter");
        register.Rows.RemoveAt(0); invoices.RefreshFromDatabase(); details=null; invoices.OpenFromReport(41);
        check(details==null && invoices.SelectedInvoice==null && invoices.IsErrorStatus,"Vanished invoice does not open an unrelated record");
    }
}
