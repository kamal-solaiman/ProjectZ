using System.IO;
using System.Windows;
using System.Windows.Controls;
using ClosedXML.Excel;
using ProjectZ.Models;
using ProjectZ.Services;
using ProjectZ.ViewModels;
using ProjectZ.Views;

internal static class MaintenanceReportUiTests
{
    private sealed class Source : IMaintenanceReportSource
    {
        public Func<ReportPeriod,CancellationToken,Task<MaintenanceReportData>> Read=(p,_)=>Task.FromResult(Sample(p));
        public Task<MaintenanceReportData> LoadAsync(ReportPeriod period,CancellationToken token)=>Read(period,token);
    }
    private static MaintenanceReportData Sample(ReportPeriod p)
    {
        var t=p.StartUtc.AddHours(3);
        var a=new ServiceTicket(31,"MNT-31",p.StartUtc.AddDays(-1),t,t,5,0,"علي",6,"مستلم",52,"عميل",true,"هاتف","ألف","أ","0000123",7);
        var b=a with { Id=32,Number="MNT-32",ReceivedUtc=t,DeliveredUtc=null,ExpectedUtc=p.StartUtc.AddDays(-1),Status=3,CustomerId=53,Priority=1,Technician="منى",Serial="0000999",WarrantyDays=0 };
        var part=new WorkshopPart(71,"شاشة","شاشات","00103","قطعة",2,3,5,"رف ب",true,10,"مورد",true);
        var stopped=part with {Id=72,Active=false,Quantity=0};
        return new(p,t.AddHours(1),new Dictionary<int,ServiceTicket>{{31,a},{32,b}},[new(31,1,"شاشة","00103",2,false,71,null),new(31,2,"يدوي","MANUAL",1,true,null,null)],
            [new(1,31,p.StartUtc.AddDays(-1),1,1,80.25m,7,"فرع",0,6,"مستلم",null,false,false),
             new(2,31,t,2,1,19.75m,8,"فرع",1,9,"منفذ",null,false,false),new(3,32,t,1,1,15m,7,"فرع",0,6,"مستلم",null,false,false),
             new(4,31,p.EndUtc,2,2,5m,7,"فرع",0,9,"منفذ",1,false,false)],
            [new(1,31,t,9,"منفذ",5),new(2,32,t,6,"مستلم",12)],new Dictionary<int,WorkshopPart>{{71,part},{72,stopped}},
            [new(7,71,2,5,0,5,t,null,11,"مورد",true,6,"مستلم"),new(8,71,3,2,5,3,t,31,10,"مورد",true,9,"منفذ"),new(9,72,4,1,0,1,p.EndUtc,31,10,"مورد",true,9,"منفذ")]);
    }
    internal static void Run(Action<bool,string> check,Action pump)
    {
        var source=new Source();int opened=0,client=0,part=0,vendor=0;
        var vm=new MaintenanceReportsViewModel(source,id=>opened=id,id=>client=id,id=>part=id,id=>vendor=id);
        var view=new DocumentReportView{DataContext=vm};vm.RefreshAsync().GetAwaiter().GetResult(); pump();
        view.Measure(new Size(980,620));view.Arrange(new Rect(0,0,980,620));view.UpdateLayout();pump();
        var grid=(DataGrid)view.FindName("Results");
        decimal? Net()=>vm.Page!.Metrics.Single(x=>x.Label=="صافي المقبوض").Value;
        void Mode(ServiceReportMode mode)=>vm.Mode=vm.Modes.Single(x=>x.Id==(int)mode);
        void Table(string name)=>vm.SelectedTable=vm.Page!.Tables.Single(t=>t.Title==name);
        check(vm.IsMovement && Net()==34.75m && grid.ActualHeight>80 && grid.Columns.Count==vm.SelectedTable!.Columns.Length && grid.Items.Count==4,
            "Service starts on dated activity: issue and delivery carry no money, no double-counted deposit activity or tomorrow's refund");
        Table("حسابات التحصيل والرد");vm.SelectedRow=vm.SelectedTable!.Rows.Single(r=>r.Display[0].Contains("#8"));vm.DrillCommand.Execute(null);
        check(vm.HasDrill && vm.SelectedTable!.Rows.Count==1 && Net()==19.75m,"Drilling the actual account keeps only its receipt, not all postings of the same order");
        vm.BackToResultsCommand.Execute(null);check(vm.SelectedTable!.Title=="حسابات التحصيل والرد","Service group back restores its analysis table");
        Mode(ServiceReportMode.Delivered);vm.Detail="00103";vm.Technician=vm.Technicians.Single(t=>t.Name=="علي");
        check(Net()==95 && vm.Page!.Tables.Single(t=>t.Title=="بنود القطع الحالية").Rows.Count==2,"Delivered cohort includes later documented refund and both manual and stocked lines on the matching ticket");
        Table("أوامر الصيانة");vm.SelectedRow=vm.SelectedTable!.Rows.Single();vm.DrillCommand.Execute(null);vm.OpenCustomerCommand.Execute(null);
        check(opened==31 && client==52,"Ticket and customer use different durable IDs");
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xlsx");
        try
        {
            ReportWorkbook.Save(path,"محل تجريبي",vm.Page!);using var book=new XLWorkbook(path);var sheet=book.Worksheet("أوامر الصيانة");
            check(sheet.RightToLeft && sheet.LastRowUsed()!.RowNumber()==2 && sheet.Cell(2,6).GetString()=="0000123" && sheet.Cell(2,2).DataType==XLDataType.DateTime
                && sheet.Cell(2,18).GetDouble()==95 && book.Worksheet("الملخص").Cell(3,1).GetString()==vm.Page!.Context,"Service Excel contains only filtered records, typed cash and dates, literal IMEI, correct mode and filters");
            var forbidden=new[]{"التكلفة","صافي الربح","المصنعية","المتبقي","التقدير","سعر القطعة","رمز الجهاز"};
            check(!book.Worksheets.SelectMany(s=>s.CellsUsed()).Where(c=>c.DataType==XLDataType.Text).Any(c=>forbidden.Any(s=>c.GetString().Contains(s))),"No pricing, unpaid balance, profit or passcode fields in any worksheet");
        }
        finally{if(File.Exists(path))File.Delete(path);}
        var paper=ReportPaper.Build("المحل",vm.Page!,vm.SelectedTable);
        var header=paper.Pages[0].GetPageRoot(false)!.Children.OfType<StackPanel>().Single().Children.OfType<TextBlock>().Select(t=>t.Text).ToArray();
        check(paper.Pages.Count>1 && header.Contains(vm.Page!.Context) && header.Any(t=>t.Contains("تقارير الصيانة")) && header.Any(t=>t.Contains("95.00")),"A4 uses the real cash total, filtered scope and all column bands");
        Mode(ServiceReportMode.Received);vm.Priority=vm.Priorities.Single(p=>p.Id==1);
        check(Net()==15 && vm.Page!.Tables.Single(t=>t.Title=="مواعيد المتابعة").Rows.Count==1 && vm.Page.Metrics.Single(m=>m.Label=="متأخر الآن").Value==1,"Unfinished overdue ticket shows its deposit but not the unpaid service price");
        Mode(ServiceReportMode.Stock);check(vm.Priority.Id is null && !vm.ShowOrderFilters && vm.Page!.Tables.Single(t=>t.Title=="نواقص المخزون الآن").Rows.Count==1,"Stock resets ticket filters and excludes archived parts from reorder alerts");
        vm.Supplier=vm.Suppliers.Single(s=>s.Id==11);Table("حركات مخزون الفترة");vm.SelectedRow=vm.SelectedTable!.Rows.Single();vm.DrillCommand.Execute(null);vm.OpenSupplierCommand.Execute(null);
        check(part==71 && vendor==11 && !vm.CanOpenCustomer && vm.Page!.Tables[0].Rows.Count==0 && vm.Page.Metrics.All(m=>m.Unit!="ج.م"),"Independent restock uses the movement supplier, no fabricated ticket/payment, and opens the actual part");
        vm.From=vm.Through!.Value.AddDays(1);vm.RefreshAsync().GetAwaiter().GetResult();check(vm.Page is null && !vm.CanExport && !vm.CanPrint,"Invalid service dates clear previous warehouse output");
        vm.From=vm.Through;source.Read=(_,_)=>throw new InvalidOperationException("فشل SQL");vm.RefreshAsync().GetAwaiter().GetResult();check(vm.Status.Contains("فشل SQL") && vm.Page is null,"Failed SQL is not reported as an empty success");
        var pending=new TaskCompletionSource<MaintenanceReportData>();source.Read=(_,_)=>pending.Task;var read=vm.RefreshAsync();vm.Leave();pending.SetResult(Sample(new(vm.From,vm.Through)));pump();
        check(read.IsCompleted && vm.Page is null && !vm.IsBusy,"Returning from another screen cannot resurrect an old service snapshot");
        // Real destinations with conflicting filters. Combined source/host tests use no shop data.
        var main=new MainViewModel(new UserSession(0,"ws","ورشة"),()=>{},maintenanceReportSource:source);
        var target=main.MaintenanceInvoices.FilteredOrders.First(o=>!o.IsWalkInCustomer && o.CustomerId>0);
        source.Read=(p,_)=>{var d=Sample(p);var o=d.Orders[32] with {Id=target.Id,Number=target.OrderNumber,CustomerId=target.CustomerId};
            return Task.FromResult(d with {Orders=new Dictionary<int,ServiceTicket>{{o.Id,o}},Activities=[],Lines=[],Payments=[d.Payments[2] with {OrderId=o.Id}],Moves=[]});};
        var menu=main.Categories.Single(c=>c.Title=="التقارير").Items[2];main.MaintenanceInvoices.SearchText="غير موجود";main.Customers.SearchText="غير موجود";
        int details=0;main.MaintenanceInvoices.OpenDetailsDialogRequested=x=>details=x.Order.Id;main.SelectItem(menu);
        main.MaintenanceReports.SelectedRow=main.MaintenanceReports.SelectedTable!.Rows.Single();main.MaintenanceReports.DrillCommand.Execute(null);
        check(main.IsMaintenanceInvoicesSelected && details==target.Id && main.MaintenanceInvoices.FilteredOrders.Contains(target),"Report opens the correct existing maintenance details despite register filters");
        main.SelectItem(menu);main.MaintenanceReports.SelectedRow=main.MaintenanceReports.SelectedTable!.Rows.Single();main.MaintenanceReports.OpenCustomerCommand.Execute(null);
        check(main.IsCustomersSelected && main.Customers.IsMaintenanceTabSelected && main.Customers.SelectedCustomerEntry?.Customer.Id==target.CustomerId,"Report opens that customer's maintenance tab, not its sales or parts tab");
        details=0;main.MaintenanceInvoices.OpenFromReport(int.MaxValue);check(details==0 && main.MaintenanceInvoices.IsErrorStatus && main.MaintenanceInvoices.SelectedOrder==null,"Missing ticket is not replaced with the first ticket");
    }
}
