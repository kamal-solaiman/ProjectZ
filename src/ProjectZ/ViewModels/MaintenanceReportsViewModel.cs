using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed record TechnicianChoice(string? Name,string Label);
public sealed class MaintenanceReportsViewModel : ReportWorkspaceViewModel<MaintenanceReportData>
{
    private static readonly TechnicianChoice AnyTech=new(null,"كل الفنيين");
    private readonly Action<int> openPart;
    private TechnicianChoice technician=AnyTech;
    private ReportChoice priority=All;
    public MaintenanceReportsViewModel(IMaintenanceReportSource? source,Action<int> openOrder,Action<int> openCustomer,Action<int> openPart,Action<int> openSupplier)
        : base(source,openOrder,openSupplier,initialMode:(int)ServiceReportMode.Movement)
    {
        this.openPart=openPart;
        OpenCustomerCommand=new ActionCommand(()=>{if(SelectedRow?.CustomerId is int id)openCustomer(id);},()=>CanOpenCustomer);
        OpenPartCommand=new ActionCommand(()=>{if(SelectedRow?.PartId is int id)openPart(id);},()=>CanOpenPart);
    }
    public override IReadOnlyList<ReportChoice> Modes { get; } = [new(1,"حركة الفترة"),new(0,"أوامر حسب الاستلام"),new(2,"أوامر حسب التسليم"),new(3,"مخزون الورشة")];
    private ServiceReportMode Basis=>(ServiceReportMode)ModeId;
    public override string SectionTitle=>MaintenanceReportProjection.Section;
    public override string ExportPrefix=>"Maintenance";
    public override string ExportMode=>Basis.ToString();
    public override bool ShowOrderFilters=>Basis!=ServiceReportMode.Stock;
    public override bool HasSupplierTools=>!ShowOrderFilters;
    public override object ExtraTools=>this;
    protected override bool ClearOnModeChange=>true;
    public override string SearchLabel=>ShowOrderFilters?"رقم الأمر، العميل، الجهاز أو IMEI":"اسم القطعة، معرّفها أو الباركود";
    public override string DetailLabel=>Basis switch {ServiceReportMode.Stock=>"التصنيف أو الموقع أو الوحدة",ServiceReportMode.Movement=>"الحركة، المنفّذ، الحساب أو القطعة",_=>"أوامر تتضمن قطعة أو باركودًا"};
    public override string Explanation=>Basis switch {
        ServiceReportMode.Stock=>"الأرصدة والنواقص الآن، والحركات داخل الفترة فقط. الكميات ليست دفعات مالية.",
        ServiceReportMode.Movement=>"كل حركة في يوم تسجيلها بتوقيت مصر ولو كان الأمر أقدم. صافي المقبوض = دفعات العميل الفعلية ناقص ما رُدّ إليه.",
        _=>"الأوامر حسب التاريخ المختار، بالحالة وقت القراءة ومع كل دفعاتها الفعلية حتى الآن، لا رصيد إقفال تاريخي." };
    public override string IssuerLabel=>"مستلم الجهاز";
    public override string AccountLabel=>IsMovement?"دفعات الحساب الفعلي فقط":"أوامر تعاملت مع الحساب (الأمر كاملًا)";
    public override string KindLabel=>Basis switch {ServiceReportMode.Stock=>"نوع حركة المخزون (للحركات فقط)",ServiceReportMode.Movement=>"نوع الحركة",_=>"الحالة الحالية للأمر"};
    public override string TableCaption=>SelectedTable is not {} t?"":$"{t.Rows.Count:N0} صف • "+(t.Grouped?(IsMovement?"افتح المجموعة لعرض حركاتها":"افتح المجموعة لعرض أوامرها"):(!ShowOrderFilters?"الانتقال للأمر أو القطعة المتاحة":"افتح الصف لعرض أمر الصيانة"));
    public ActionCommand OpenCustomerCommand {get;}
    public ActionCommand OpenPartCommand {get;}
    public bool CanOpenCustomer=>SelectedRow?.CustomerId is not null;
    public bool CanOpenPart=>SelectedRow?.PartId is not null;
    protected override void NotifyRowActions() {Changed(nameof(CanOpenCustomer));Changed(nameof(CanOpenPart));OpenCustomerCommand.NotifyCanExecuteChanged();OpenPartCommand.NotifyCanExecuteChanged();}
    protected override bool CanOpenDetail(ReportRow row)=>base.CanOpenDetail(row) || row.PartId.HasValue;
    protected override void OpenDetail(ReportRow row) { if(base.CanOpenDetail(row))base.OpenDetail(row);else if(row.PartId is int id)openPart(id); }
    protected override IReadOnlyList<ReportChoice> StatusChoices {get;}=Enumerable.Range(0,7).Select(i=>new ReportChoice(i,ServiceTicket.StatusText(i))).Prepend(All).ToArray();
    protected override IReadOnlyList<ReportChoice> EventChoices {get;}=MaintenanceReportProjection.EventNames.Select((name,i)=>new ReportChoice(i,name)).Prepend(All).ToArray();
    private static readonly ReportChoice[] StockKinds=Enumerable.Range(1,6).Select(i=>new ReportChoice(i,WorkshopMove.KindText(i))).Prepend(All).ToArray();
    public override IReadOnlyList<ReportChoice> KindOptions=>ShowOrderFilters?base.KindOptions:StockKinds;
    protected override IEnumerable<ReportChoice> GetIssuers(MaintenanceReportData d)=>d.Orders.Values.Select(o=>new ReportChoice(o.ReceiverId,$"{o.Receiver} (#{o.ReceiverId})"));
    protected override IEnumerable<ReportChoice> GetAccounts(MaintenanceReportData d)=>d.Payments.Select(p=>new ReportChoice(p.AccountId,p.AccountLabel));
    protected override IEnumerable<ReportChoice> GetSuppliers(MaintenanceReportData d)=>d.Parts.Values.Select(p=>(p.SupplierId,p.Supplier)).Concat(d.Moves.Where(m=>d.Period.Contains(m.AtUtc)).Select(m=>(m.SupplierId,m.Supplier)))
        .Where(s=>s.SupplierId.HasValue).Select(s=>new ReportChoice(s.SupplierId,$"{s.Supplier} (#{s.SupplierId})"));
    public IReadOnlyList<ReportChoice> Priorities {get;}=Enumerable.Range(0,3).Select(i=>new ReportChoice(i,ServiceTicket.PriorityText(i))).Prepend(All).ToArray();
    public IReadOnlyList<TechnicianChoice> Technicians {get;private set;}=[AnyTech];
    public TechnicianChoice Technician {get=>technician; set{if(value==null||technician==value)return;technician=value;Changed();ResetDrill();}}
    public ReportChoice Priority {get=>priority; set{if(value==null||priority==value)return;priority=value;Changed();ResetDrill();}}
    protected override void ClearExtraFilters(){priority=All;technician=AnyTech;Changed(nameof(Priority));Changed(nameof(Technician));}
    protected override void SetExtraChoices(MaintenanceReportData d)
    {
        Technicians=d.Orders.Values.Select(o=>new TechnicianChoice(o.Technician,o.TechLabel)).Distinct().OrderBy(t=>t.Label).Prepend(AnyTech).Append(technician).Distinct().ToArray();
        technician=Technicians.First(t=>t==technician);Changed(nameof(Technicians));Changed(nameof(Technician));
    }
    protected override ReportPage Project(MaintenanceReportData d,bool movement,ReportFilter f,string currency)=>MaintenanceReportProjection.Build(d,Basis,f,currency,technician.Name,priority.Id);
}
