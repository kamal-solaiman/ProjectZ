namespace ProjectZ.Models;

/// <summary>Ticket cohorts, dated postings, and independent warehouse quantities. Only payment rows carry money.</summary>
public static class MaintenanceReportProjection
{
    public const string Section = "تقارير الصيانة";
    public static readonly string[] EventNames = ["عربون / دفعة تحت الحساب","تحصيل عند التسليم","رد للعميل","استلام جهاز","تحديث الورشة والفحص",
        "تسليم جهاز","إلغاء أمر","إعادة فتح","نشاط صرف بند","إضافة بند يدوي","إزالة بند","معاينة / طباعة","نشاط تحصيل بلا تفاصيل دفعة مطابقة","نشاط محفوظ","صرف من المخزون","إرجاع إلى المخزون"];
    private static bool Has(string text,string term)=>text.Contains(term,StringComparison.OrdinalIgnoreCase);
    private static DateTime Local(DateTime t)=>ReportPeriod.Local(t);
    private static DateTime? Local(DateTime? t)=>t.HasValue?Local(t.Value):null;
    private static string Person(int id,string name)=>$"{name} (#{id})";
    private static string Vendor(int? id,string name)=>id.HasValue?Person(id.Value,name):"غير محدد";
    private static ReportTable Table(string name,string columns,IEnumerable<ReportRow> rows,bool grouped=false)=>new(name,columns.Split('|'),rows.ToArray(),grouped);
    private static ReportRow Row(ServiceTicket o,object?[] cells,int? part=null)=>new([o.Id],cells){CustomerId=o.CustomerActive?o.CustomerId:null,PartId=part};
    private sealed record Fact(int Id,int OrderId,int Kind,DateTime AtUtc,int ActorId,string Actor,ServicePayment? Payment=null,WorkshopMove? Move=null,byte? RecordedStatus=null)
    {
        public ReportEventKey Key=>new(Kind,Id);
        public string Label=>EventNames[Kind];
    }
    private static IEnumerable<Fact> Facts(MaintenanceReportData d)
    {
        // A collect/change activity mirrors a payment/stock operation. Preserve an orphaned activity as history,
        // but never infer money or units from its text, and never count an audit log as another posting.
        var deposits=d.Payments.Where(p=>p.EventType==0).Select(p=>(p.OrderId,p.AtUtc,p.ActorId)).ToHashSet();
        var moves=d.Moves.Where(m=>m.OrderId.HasValue).ToLookup(m=>(m.OrderId!.Value,m.AtUtc,m.ActorId,m.Kind));
        foreach(var p in d.Payments) yield return new(p.Id,p.OrderId,p.EventType,p.AtUtc,p.ActorId,p.Actor,p);
        foreach(var a in d.Activities)
        {
            if(a.EventType==12 && deposits.Contains((a.OrderId,a.AtUtc,a.ActorId)))continue;
            if((a.EventType==8 || a.EventType==10) && moves.Contains((a.OrderId,a.AtUtc,a.ActorId,(byte)(a.EventType==8?3:4))))continue;
            yield return new(a.Id,a.OrderId,a.EventType,a.AtUtc,a.ActorId,a.Actor,RecordedStatus:a.RecordedStatus);
        }
        foreach(var m in d.Moves.Where(m=>m.OrderId.HasValue && m.Kind is 3 or 4))
            yield return new(m.Id,m.OrderId!.Value,m.Kind==3?14:15,m.AtUtc,m.ActorId,m.Actor,Move:m);
    }
    private readonly record struct Cash(decimal Deposit,decimal Delivery,decimal Refund)
    {
        public decimal Net=>Deposit+Delivery-Refund;
        public static Cash Of(IEnumerable<ServicePayment> payments)
        {
            decimal deposit=0,delivery=0,refund=0;
            foreach(var p in payments) { if(p.Kind==2)refund+=p.Amount; else if(p.Stage==1)deposit+=p.Amount; else delivery+=p.Amount; }
            return new(deposit,delivery,refund);
        }
        public ReportMetric[] Metrics(string unit)=>[new("عربون ودفعات تحت الحساب",Deposit,unit),new("محصّل عند التسليم",Delivery,unit),new("مردود للعميل",Refund,unit),new("صافي المقبوض",Net,unit)];
    }
    public static ReportPage Build(MaintenanceReportData d,ServiceReportMode mode,ReportFilter f,string currency,string? technician=null,int? priority=null)
    {
        string search=f.Search.Trim(),detail=f.Detail.Trim();
        string context=$"{d.Period.Label} | قراءة {Local(d.ReadAtUtc):yyyy-MM-dd HH:mm}"
            +(search.Length>0?" | بحث: "+search:"")+(detail.Length>0?" | تفصيل: "+detail:"");
        if(mode==ServiceReportMode.Stock)return Stock(d,f,search,detail,context);
        bool movement=mode==ServiceReportMode.Movement;
        context+=$" | {currency}"+(f.IssuerId is int user?$" | مستلم الجهاز #{user}":"")
            +(technician is null?"":" | الفني الحالي: "+(technician.Length>0?technician:"غير مسند"))+(priority is int p?" | "+ServiceTicket.PriorityText(p):"")
            +(f.AccountId is int acc?$" | {(movement?"حركات الحساب":"أوامر تعاملت مع الحساب")} #{acc}":"")
            +(f.StatusOrKind is int k?" | "+(movement?EventNames[k]:ServiceTicket.StatusText(k)):"")+(f.InvoiceIds is null?"":" | المجموعة: "+f.DrillLabel);
        bool Match(ServiceTicket o)=>(f.InvoiceIds is null || f.InvoiceIds.Contains(o.Id)) && (f.IssuerId is null || f.IssuerId==o.ReceiverId)
            && (priority is null || priority==o.Priority) && (technician is null || technician==o.Technician)
            && (search.Length==0 || Has(o.Number,search) || Has(o.CustomerLabel,search) || Has(o.Serial,search) || Has(o.Device,search));
        IEnumerable<Fact> facts=Facts(d);
        if(movement)
        {
            var events=facts.Where(e=>d.Period.Contains(e.AtUtc) && Match(d.Orders[e.OrderId]) && (f.EventKeys is null || f.EventKeys.Contains(e.Key))
                && (f.StatusOrKind is null || f.StatusOrKind==e.Kind) && (f.AccountId is null || e.Payment?.AccountId==f.AccountId)
                && (detail.Length==0 || Has(Person(e.ActorId,e.Actor),detail) || Has(e.Label,detail) || Has(e.Payment?.AccountLabel??"",detail)
                    || (e.Move is {} m && (Has(d.Parts[m.PartId].Label,detail) || Has(d.Parts[m.PartId].Barcode,detail)))))
                .OrderByDescending(e=>e.AtUtc).ThenBy(e=>e.Kind).ThenByDescending(e=>e.Id).ToArray();
            return Movement(d,events,context,currency);
        }
        var partMatches=detail.Length==0?null:d.Lines.Where(l=>Has(l.Name,detail)||Has(l.Barcode,detail)).Select(l=>l.OrderId).ToHashSet();
        var accounts=f.AccountId is null?null:d.Payments.Where(p=>p.AccountId==f.AccountId).Select(p=>p.OrderId).ToHashSet();
        var orders=d.Orders.Values.Where(o=>Match(o) && (mode==ServiceReportMode.Received?d.Period.Contains(o.ReceivedUtc):o.Status==5 && o.DeliveredUtc is DateTime at && d.Period.Contains(at))
            && (f.StatusOrKind is null || f.StatusOrKind==o.Status) && (partMatches is null || partMatches.Contains(o.Id)) && (accounts is null || accounts.Contains(o.Id)))
            .OrderByDescending(o=>mode==ServiceReportMode.Received?o.ReceivedUtc:o.DeliveredUtc).ThenByDescending(o=>o.Id).ToArray();
        var ids=orders.Select(o=>o.Id).ToHashSet(); var history=facts.Where(e=>ids.Contains(e.OrderId)).OrderByDescending(e=>e.AtUtc).ThenBy(e=>e.Kind).ThenByDescending(e=>e.Id).ToArray();
        var payments=d.Payments.Where(p=>ids.Contains(p.OrderId)).ToLookup(p=>p.OrderId);
        var lines=d.Lines.Where(l=>ids.Contains(l.OrderId)).ToArray();
        bool Late(ServiceTicket o)=>o.Status<5 && o.ExpectedUtc is DateTime due && Local(due).Date<Local(d.ReadAtUtc).Date;
        decimal? Hours(ServiceTicket o)=>o.Status==5 && o.DeliveredUtc is DateTime end && end>=o.ReceivedUtc?decimal.Round((decimal)(end-o.ReceivedUtc).TotalHours,1):null;
        ReportTable By<TKey>(string title,Func<ServiceTicket,TKey> key,Func<ServiceTicket,object> label)=>Table(title,
            "البيان|أوامر|نشطة الآن|متأخرة الآن|مسلّمة الآن|ملغاة الآن|متوسط ساعات التسليم|عربون ودفعات|تحصيل التسليم|رد فعلي|صافي المقبوض",orders.GroupBy(key).Select(g=> {
                var cash=Cash.Of(g.SelectMany(o=>payments[o.Id]));
                return Group(d,g.Select(o=>o.Id),[label(g.First()),g.Count(),g.Count(o=>o.Status<5),g.Count(Late),g.Count(o=>o.Status==5),g.Count(o=>o.Status==6),g.Average(Hours) is decimal avg ? decimal.Round(avg,1) : null,cash.Deposit,cash.Delivery,cash.Refund,cash.Net]); }),true);
        var all=Cash.Of(payments.SelectMany(x=>x));
        List<ReportTable> tables=[Table("أوامر الصيانة","الأمر|الاستلام (مصر)|التسليم (مصر)|العميل|الجهاز|السيريال أو IMEI|الحالة الآن|الأولوية|الفني الحالي|مستلم الجهاز|موعد التسليم المحدد|متأخر الآن|ساعات حتى التسليم|أيام ضمان التسليم|عربون ودفعات|تحصيل التسليم|مردود|صافي المقبوض",
            orders.Select(o=>{var c=Cash.Of(payments[o.Id]);return Row(o,[o.Number,Local(o.ReceivedUtc),Local(o.DeliveredUtc),o.CustomerLabel,o.Device,o.Serial,ServiceTicket.StatusText(o.Status),ServiceTicket.PriorityText(o.Priority),o.TechLabel,Person(o.ReceiverId,o.Receiver),Local(o.ExpectedUtc),Late(o)?"نعم":"",Hours(o),o.Status==5?o.WarrantyDays:null,c.Deposit,c.Delivery,c.Refund,c.Net]);})),
            By("الفنيون",o=>o.Technician,o=>o.TechLabel),By("حالات الأوامر",o=>o.Status,o=>ServiceTicket.StatusText(o.Status)),By("العملاء",o=>o.CustomerId,o=>o.CustomerId.HasValue?o.CustomerLabel:"عملاء دون ملف"),By("الأجهزة",o=>(o.Type,o.Brand,o.Model),o=>o.Device),
            By("موظفو الاستلام",o=>o.ReceiverId,o=>Person(o.ReceiverId,o.Receiver)),By("الأولويات",o=>o.Priority,o=>ServiceTicket.PriorityText(o.Priority)),
            Table("التسليم والضمان","الأمر|العميل|الجهاز|الفني الحالي|الاستلام|الموعد المحدد|التسليم الفعلي|ساعات من الاستلام|التسليم بالنسبة ليوم الموعد|أيام الضمان المسجلة",
                orders.Where(o=>o.Status==5 && o.DeliveredUtc.HasValue).Select(o=>Row(o,[o.Number,o.CustomerLabel,o.Device,o.TechLabel,Local(o.ReceivedUtc),Local(o.ExpectedUtc),Local(o.DeliveredUtc),Hours(o),
                    o.ExpectedUtc is DateTime due ? (Local(o.DeliveredUtc!.Value).Date>Local(due).Date?"بعد الموعد":"ضمن الموعد") : "بلا موعد محدد",o.WarrantyDays]))),
            Table("مواعيد المتابعة","الأمر|العميل|الجهاز|الحالة|الأولوية|الفني الحالي|الاستلام|الموعد المحدد|تجاوز الموعد",orders.Where(o=>o.Status<5).OrderBy(o=>o.ExpectedUtc??DateTime.MaxValue).Select(o=>Row(o,[o.Number,o.CustomerLabel,o.Device,ServiceTicket.StatusText(o.Status),ServiceTicket.PriorityText(o.Priority),o.TechLabel,Local(o.ReceivedUtc),Local(o.ExpectedUtc),Late(o)?"نعم":""]))),
            Table("بنود القطع الحالية","الأمر|رقم البند|القطعة|الباركود|الكمية|مصدر البند",lines.Select(l=>Row(d.Orders[l.OrderId],[d.Orders[l.OrderId].Number,l.Position,l.Name,l.Barcode,l.Qty,l.Manual?"يدوي":l.PartId.HasValue?"مخزون الورشة":"مرجع صنف قديم"],ActivePart(d,l.PartId)))),
            Ledger("الدفعات والردود",d,history.Where(e=>e.Payment is not null)),Accounts(d,history),Ledger("سجل الأمر",d,history)];
        return new(mode==ServiceReportMode.Received?"أوامر حسب الاستلام • الحالة وقت القراءة":"أوامر حسب التسليم الحالي • الحالة وقت القراءة",context,
            [new("عدد الأوامر",orders.Length,"أمر"),new("متأخر الآن",orders.Count(Late),"أمر"),..all.Metrics(currency)],tables,Warning(history),Section);
    }
    private static int? ActivePart(MaintenanceReportData d,int? id)=>id is int n && d.Parts.TryGetValue(n,out var p) && p.Active?n:null;
    private static ReportRow Group(MaintenanceReportData d,IEnumerable<int> orderIds,object?[] cells,ReportEventKey[]? keys=null)
    {
        var ids=orderIds.Distinct().ToArray();var o=d.Orders[ids[0]];
        return new(ids,cells){EventKeys=keys,CustomerId=o.CustomerActive && ids.All(id=>d.Orders[id].CustomerId==o.CustomerId)?o.CustomerId:null};
    }
    private static string Warning(IEnumerable<Fact> facts)
    {
        var issues=facts.Count(e=>e.Payment is {} p && (p.RefundNeedsReview || p.LedgerNeedsReview));
        return issues==0?"":$"تحتاج {issues} حركة مالية إلى مطابقة الإيصال الأصلي أو دفتر الحساب. المعروض هو المبلغ المسجّل، دون افتراض دفعة أخرى.";
    }
    private static ReportTable Ledger(string title,MaintenanceReportData d,IEnumerable<Fact> facts)=>Table(title,
        "الأمر|التوقيت (مصر)|الحركة|مرجع الصف|العميل|المنفّذ|حساب الدفعة الفعلي|عربون ودفعات|تحصيل تسليم|رد للعميل|معرّف الدفعة المردودة|القطعة|الكمية|مرحلة الأمر وقت النشاط|المطابقة",facts.Select(e=>{
            var o=d.Orders[e.OrderId]; var p=e.Payment;var m=e.Move;
            return Row(o,[o.Number,Local(e.AtUtc),e.Label,(p is not null?"دفعة #":m is not null?"مخزون #":"نشاط #")+e.Id,o.CustomerLabel,Person(e.ActorId,e.Actor),p?.AccountLabel,
                e.Kind==0?p?.Amount:null,e.Kind==1?p?.Amount:null,e.Kind==2?p?.Amount:null,p?.OriginalReceipt,m is null?null:d.Parts[m.PartId].Label,m?.Qty,e.RecordedStatus is byte st?ServiceTicket.StatusText(st):null,
                string.Join(" · ",new[]{p?.RefundNeedsReview==true?"رد يحتاج مطابقة أصل الدفعة":"",p?.LedgerNeedsReview==true?"اختلاف مع دفتر الحساب":""}.Where(x=>x.Length>0))],ActivePart(d,m?.PartId)); }));
    private static ReportTable Accounts(MaintenanceReportData d,IEnumerable<Fact> events)=>Table("حسابات التحصيل والرد","الحساب|أوامر|دفعات وردود|عربون ودفعات|تحصيل التسليم|الرد|صافي المقبوض",
        events.Where(e=>e.Payment!=null).GroupBy(e=>e.Payment!.AccountId).Select(g=>{var c=Cash.Of(g.Select(e=>e.Payment!));return Group(d,g.Select(e=>e.OrderId),[g.First().Payment!.AccountLabel,g.Select(e=>e.OrderId).Distinct().Count(),g.Count(),c.Deposit,c.Delivery,c.Refund,c.Net],g.Select(e=>e.Key).ToArray());}),true);
    private static ReportPage Movement(MaintenanceReportData d,Fact[] events,string context,string currency)
    {
        ReportTable By<TKey>(string title,Func<Fact,TKey> key,Func<Fact,object> label)=>Table(title,"البيان|أوامر متأثرة|أسطر حركة|عربون ودفعات|تحصيل التسليم|الرد|صافي المقبوض",
            events.GroupBy(key).Select(g=>{var c=Cash.Of(g.Where(e=>e.Payment!=null).Select(e=>e.Payment!));return Group(d,g.Select(e=>e.OrderId),[label(g.First()),g.Select(e=>e.OrderId).Distinct().Count(),g.Count(),c.Deposit,c.Delivery,c.Refund,c.Net],g.Select(e=>e.Key).ToArray());}),true);
        var cash=Cash.Of(events.Where(e=>e.Payment!=null).Select(e=>e.Payment!));
        return new("حركة الفترة • بتاريخ تسجيل كل عملية",context,[new("أوامر متأثرة",events.Select(e=>e.OrderId).Distinct().Count(),"أمر"),new("أسطر الحركة",events.Length,"سطر"),..cash.Metrics(currency)],
            [Ledger("دفتر الحركات",d,events),Ledger("الدفعات والردود",d,events.Where(e=>e.Payment!=null)),By("الحركة اليومية",e=>DateOnly.FromDateTime(Local(e.AtUtc)),e=>DateOnly.FromDateTime(Local(e.AtUtc))),
             By("أنواع الحركات",e=>e.Kind,e=>e.Label),By("منفّذو الحركات",e=>e.ActorId,e=>Person(e.ActorId,e.Actor)),Accounts(d,events)],Warning(events),Section);
    }
    private static ReportPage Stock(MaintenanceReportData d,ReportFilter f,string search,string detail,string context)
    {
        bool Part(WorkshopPart p)=>(Has(p.Label,search)||Has(p.Barcode,search)) && (Has(p.Category,detail)||Has(p.Location,detail)||Has(p.Unit,detail));
        var parts=d.Parts.Values.Where(p=>Part(p) && (f.SupplierId==null||f.SupplierId==p.SupplierId)).OrderBy(p=>p.Name).ThenBy(p=>p.Id).ToArray();
        var moves=d.Moves.Where(m=>d.Period.Contains(m.AtUtc) && Part(d.Parts[m.PartId]) && (f.StatusOrKind==null || m.Kind==f.StatusOrKind)
            && (f.SupplierId==null || m.SupplierId==f.SupplierId)).OrderByDescending(m=>m.AtUtc).ThenByDescending(m=>m.Id).ToArray();
        ReportTable Inventory(string title,IEnumerable<WorkshopPart> items)=>Table(title,"القطعة|التصنيف|الباركود|الوحدة|الرصيد الآن|حد التنبيه|كمية إعادة الطلب|الموقع|المورد الحالي|الحالة",items.Select(p=>new ReportRow([],
            [p.Label,p.Category,p.Barcode,p.Unit,p.Quantity,p.Minimum,p.Reorder,p.Location,Vendor(p.SupplierId,p.Supplier),p.Active?"نشطة":"مؤرشفة"]){CanOpenInvoice=false,PartId=p.Active?p.Id:null,SupplierId=p.SupplierActive?p.SupplierId:null}));
        return new("مخزون الورشة • أرصدة الآن وحركات الفترة",context+(f.SupplierId.HasValue?$" | مورد #{f.SupplierId} (الحالي للرصيد، والمسجّل للحركة)":"")
            +(f.StatusOrKind is int kind?" | نوع الحركة فقط: "+WorkshopMove.KindText(kind):""),[new("أصناف نشطة",parts.Count(p=>p.Active),"صنف"),new("عند حد التنبيه أو دونه",parts.Count(p=>p.Low),"صنف"),
            new("أصناف نفدت",parts.Count(p=>p.Active && p.Quantity==0),"صنف"),new("حركات الفترة",moves.Length,"حركة")],
            [Inventory("رصيد الورشة الآن",parts),Inventory("نواقص المخزون الآن",parts.Where(p=>p.Low)),
             Table("ملخص حركة القطع","القطعة|الوحدة|أسطر الحركة|رصيد افتتاحي مسجّل|توريد|صرف|إرجاع|تسوية زيادة|تسوية نقص|صافي تغير الكمية",moves.GroupBy(m=>m.PartId).Select(g=>{
                 var p=d.Parts[g.Key];long Qty(int movementKind)=>g.Where(m=>m.Kind==movementKind).Sum(m=>(long)m.Qty);
                 return new ReportRow([],[p.Label,p.Unit,g.Count(),Qty(1),Qty(2),Qty(3),Qty(4),Qty(5),Qty(6),g.Sum(m=>(long)m.After-m.Before)]){CanOpenInvoice=false,PartId=p.Active?p.Id:null};})),
             Table("حركات مخزون الفترة","القطعة|التوقيت (مصر)|النوع|رقم الحركة|الوحدة|الكمية|الرصيد قبل|الرصيد بعد|مورد الحركة|الأمر|عميل الحركة|المنفّذ",moves.Select(m=>{
                 var p=d.Parts[m.PartId];var o=m.OrderId.HasValue?d.Orders[m.OrderId.Value]:null;
                 return new ReportRow(o==null?[]:[o.Id],[p.Label,Local(m.AtUtc),WorkshopMove.KindText(m.Kind),m.Id,p.Unit,m.Qty,m.Before,m.After,Vendor(m.SupplierId,m.Supplier),o?.Number,m.CustomerId is int cid?Person(cid,m.Customer):null,Person(m.ActorId,m.Actor)])
                    {CanOpenInvoice=o!=null,CustomerId=m.CustomerActive?m.CustomerId:null,PartId=p.Active?p.Id:null,SupplierId=m.SupplierActive?m.SupplierId:null};}))],"",Section);
    }
}
