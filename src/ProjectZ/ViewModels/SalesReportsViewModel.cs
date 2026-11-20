using ProjectZ.Helpers;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.ViewModels;

public sealed class SalesReportsViewModel : ObservableObject
{
    public sealed record Choice(int? Id, string Label);
    private static readonly Choice Everyone = new(null,"كل الكاشيرية");
    private static readonly Choice EveryAccount = new(null,"كل الحسابات");
    private static readonly Choice EveryKind = new(null,"الكل");
    private static readonly Choice[] StatusChoices = Enumerable.Range(0,6).Select(k=>new Choice(k,ReportInvoice.StatusText((byte)k))).Prepend(EveryKind).ToArray();
    private static readonly Choice[] EventChoices = Enum.GetValues<SalesEventKind>().Select(k=>new Choice((int)k,SalesReportEvent.KindText(k))).Prepend(EveryKind).ToArray();
    private Choice selectedKind = EveryKind, selectedAccount = EveryAccount;
    private readonly ISalesReportSource? source;
    private readonly Action<int> openInvoice;
    private CancellationTokenSource? loading;
    private SalesReportData? data;
    private SalesReportPage? page;
    private SalesReportTable? table;
    private SalesReportRow? row;
    private bool movement;
    private DateTime? from = SalesReportPeriod.Today, through = SalesReportPeriod.Today;
    private string search = "", detail = "", status = "", drillLabel = "";
    private Choice cashier = Everyone;
    private HashSet<int>? drill;
    private HashSet<SalesEventKey>? eventDrill;
    public SalesReportsViewModel(ISalesReportSource? source, Action<int> openInvoice)
    {
        this.source=source; this.openInvoice=openInvoice;
        ApplyCommand=new AsyncCommand(RefreshAsync);
        DrillCommand=new ActionCommand(OpenRow, () => row is not null);
        ClearCommand=new RelayCommand(() => { search=detail=drillLabel=""; cashier=Everyone; selectedKind=EveryKind; selectedAccount=EveryAccount; drill=null; eventDrill=null;
            Changed(nameof(SelectedKind)); Changed(nameof(SelectedAccount));
            Changed(nameof(Search)); Changed(nameof(Detail)); Changed(nameof(Cashier)); Rebuild(); });
    }
    public AsyncCommand ApplyCommand { get; }
    public ActionCommand DrillCommand { get; }
    public RelayCommand ClearCommand { get; }
    public IReadOnlyList<Choice> Cashiers { get; private set; } = [Everyone];
    public IReadOnlyList<Choice> Accounts { get; private set; } = [EveryAccount];
    public IReadOnlyList<Choice> KindOptions => movement ? EventChoices : StatusChoices;
    public Choice SelectedKind { get=>selectedKind; set { if(value is null || value==selectedKind)return; selectedKind=value; Changed(); ResetDrill(); } }
    public Choice SelectedAccount { get=>selectedAccount; set { if(value is null || value==selectedAccount)return; selectedAccount=value; Changed(); ResetDrill(); } }
    public string KindLabel => movement ? "نوع الحركة" : "حالة الفاتورة";
    public string AccountLabel => movement ? "حركات الحساب الفعلي فقط" : "فواتير تعاملت مع الحساب (الفاتورة كاملة)";
    public Choice Cashier { get=>cashier; set { if(value is null || cashier==value)return; cashier=value; Changed(); ResetDrill(); } }
    public string Search { get=>search; set { if(search==value)return; search=value??""; Changed(); ResetDrill(); } }
    public string Detail { get=>detail; set { if(detail==value)return; detail=value??""; Changed(); ResetDrill(); } }
    public DateTime? From { get=>from; set { if(from==value)return; from=value; Changed(); InvalidateDates(); } }
    public DateTime? Through { get=>through; set { if(through==value)return; through=value; Changed(); InvalidateDates(); } }
    public bool IsMovement { get=>movement; set { if(movement==value)return; movement=value; detail=""; selectedKind=EveryKind; Changed(); Changed(nameof(IsCohort));
        Changed(nameof(KindOptions)); Changed(nameof(SelectedKind)); Changed(nameof(KindLabel)); Changed(nameof(AccountLabel));
        Changed(nameof(DetailLabel)); Changed(nameof(Explanation)); Changed(nameof(Detail)); ResetDrill(); } }
    public bool IsCohort { get=>!movement; set { if(value)IsMovement=false; } }
    public string DetailLabel => movement ? "نوع الحركة، المنفذ، الحساب أو البيان" : "فواتير تتضمن صنفًا، تصنيفًا أو باركودًا";
    public string Explanation => movement ? "دفتر الحركة بتوقيت مصر • قيمة البضاعة منفصلة عن الأموال المحصلة أو المردودة فعليًا." :
        "فواتير صادرة في الفترة بأرصدة اليوم • يشمل دفعاتها ومرتجعاتها اللاحقة، وليس رصيدًا تاريخيًا عند نهاية الفترة.";
    public string SourceLabel => source is null ? "معاينة • غير متصل" : "المصدر • SQL Server";
    public string Status { get=>status; private set { status=value; Changed(); } }
    public bool IsBusy => loading is not null;
    public SalesReportPage? Page => page;
    public bool CanExport => page?.Tables.Any(t=>t.Rows.Count>0)==true;
    public bool CanPrint => table?.Rows.Count>0;
    public SalesReportTable? SelectedTable { get=>table; set { table=value; SelectedRow=null; Changed(); Changed(nameof(CanPrint)); Changed(nameof(TableCaption)); } }
    public string TableCaption => table is null ? "" : $"{table.Rows.Count:N0} صف • " + (table.Grouped ? (movement ? "افتح المجموعة لعرض حركاتها" : "افتح المجموعة لعرض فواتيرها") : "افتح الصف للانتقال إلى الفاتورة");
    public SalesReportRow? SelectedRow { get=>row; set { row=value; Changed(); DrillCommand.NotifyCanExecuteChanged(); } }
    private void ResetDrill() { drill=null; eventDrill=null; drillLabel=""; Rebuild(); }
    private void InvalidateDates()
    {
        Leave(); data=null; ResetDrill(); Status="تغيّرت الفترة. اضغط «تحميل الفترة» قبل قراءة النتائج أو تصديرها.";
    }
    public void Leave()
    {
        var previous=loading; loading=null; previous?.Cancel(); Changed(nameof(IsBusy));
    }
    public async Task RefreshAsync()
    {
        Leave(); data=null; ResetDrill();
        if(source is null) { Status="لا يوجد اتصال في وضع المعاينة؛ لا تُعرض أرصدة تجريبية."; return; }
        using var request=new CancellationTokenSource(); loading=request; Changed(nameof(IsBusy));
        try
        {
            var period=new SalesReportPeriod(From,Through);
            Status="جارٍ قراءة الفواتير والحركات من قاعدة البيانات…";
            var result=await source.LoadAsync(period,request.Token);
            if(request.IsCancellationRequested)return;
            var options=result.Invoices.Select(i=>i.Doc).Concat(result.Events.Select(e=>e.Doc)).DistinctBy(d=>d.CashierId)
                .OrderBy(d=>d.Cashier).Select(d=>new Choice(d.CashierId,$"{d.Cashier} (#{d.CashierId})")).Prepend(Everyone).ToArray();
            // Keep an explicit cashier filter on refresh, even if it now has zero matches.
            if(!options.Any(o=>o.Id==cashier.Id)) options=[..options,cashier];
            Cashiers=options; cashier=options.First(o=>o.Id==cashier.Id); Changed(nameof(Cashiers)); Changed(nameof(Cashier));
            var accounts=result.Events.Where(e=>e.AccountId.HasValue).DistinctBy(e=>e.AccountId).OrderBy(e=>e.AccountKind).ThenBy(e=>e.AccountId)
                .Select(e=>new Choice(e.AccountId,e.AccountLabel)).Prepend(EveryAccount).ToArray();
            if(!accounts.Any(a=>a.Id==selectedAccount.Id)) accounts=[..accounts,selectedAccount];
            Accounts=accounts; selectedAccount=accounts.First(a=>a.Id==selectedAccount.Id); Changed(nameof(Accounts)); Changed(nameof(SelectedAccount));
            data=result; Rebuild(); Status="تم تحميل البيانات • " + result.Period.Label;
        }
        catch(Exception) when(request.IsCancellationRequested) { }
        catch(Exception ex) { data=null; Rebuild(); Status="تعذر عرض التقرير: " + ex.Message; }
        finally { if(ReferenceEquals(loading,request)) { loading=null; Changed(nameof(IsBusy)); } }
    }
    public async Task PresetAsync(int days)
    {
        var today=SalesReportPeriod.Today; From=days==0 ? new DateTime(today.Year,today.Month,1) : today.AddDays(1-days); Through=today;
        await RefreshAsync();
    }
    private void Rebuild()
    {
        var title=table?.Title;
        page=data is null ? null : SalesReportProjection.Build(data,movement,new(search,cashier.Id,detail,drill,drillLabel,selectedKind.Id,selectedAccount.Id,eventDrill),StoreIdentity.Current.CurrencyLabel);
        Changed(nameof(Page)); SelectedTable=page?.Tables.FirstOrDefault(t=>t.Title==title) ?? page?.Tables.FirstOrDefault(); Changed(nameof(CanExport));
    }
    private void OpenRow()
    {
        if(row is null || table is null)return;
        if(table.Grouped) { drill=row.InvoiceIds.ToHashSet(); eventDrill=movement ? row.EventKeys?.ToHashSet() : null; drillLabel=row.Display[0]; Rebuild(); SelectedTable=page?.Tables.FirstOrDefault(); }
        else if(row.InvoiceIds.Length==1) openInvoice(row.InvoiceIds[0]);
    }
}
