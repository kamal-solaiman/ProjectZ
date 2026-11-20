namespace ProjectZ.Models;

// No service prices, costs, due amounts, free-text workshop notes or device unlock credentials
// belong to this contract. The only monetary field is the amount on a saved customer payment row.
public sealed record ServiceTicket(int Id, string Number, DateTime ReceivedUtc, DateTime? DeliveredUtc, DateTime? ExpectedUtc,
    byte Status, byte Priority, string Technician, int ReceiverId, string Receiver, int? CustomerId, string Customer,
    bool CustomerActive, string Type, string Brand, string Model, string Serial, int WarrantyDays)
{
    public string Device => string.Join(" · ", new[]{Type, Brand, Model}.Where(x=>!string.IsNullOrWhiteSpace(x)));
    public string CustomerLabel => CustomerId is int id ? $"{Customer} (#{id})" : $"{Customer} (دون ملف)";
    public string TechLabel => Technician.Length==0 ? "غير مسند" : Technician;
    public static string StatusText(int status) => status switch { 0=>"تم الاستلام",1=>"قيد الفحص",2=>"قيد الإصلاح",3=>"بانتظار قطع",4=>"جاهز للتسليم",5=>"تم التسليم",6=>"ملغى",_=>"غير معروف" };
    public static string PriorityText(int p) => p switch { 0=>"عادي",1=>"مستعجل",2=>"ضمان / إعادة فحص",_=>"غير معروف" };
}
public sealed record ServiceLine(int OrderId,int Position,string Name,string Barcode,int Qty,bool Manual,int? PartId,int? LegacyProductId);
public sealed record ServicePayment(int Id,int OrderId,DateTime AtUtc,byte Stage,byte Kind,decimal Amount,int AccountId,
    string Account,byte Channel,int ActorId,string Actor,int? OriginalReceipt,bool RefundNeedsReview,bool LedgerNeedsReview)
{
    public int EventType => Kind==2 ? 2 : Stage==2 ? 1 : 0;
    public string AccountLabel => $"{Channel switch {0=>"نقدي",1=>"فودافون كاش",2=>"إنستاباي",_=>"حساب"}} · {Account} (#{AccountId})";
}
public sealed record ServiceActivity(int Id,int OrderId,DateTime AtUtc,int ActorId,string Actor,int EventType);
public sealed record WorkshopPart(int Id,string Name,string Category,string Barcode,string Unit,int Quantity,int Minimum,int Reorder,
    string Location,bool Active,int? SupplierId,string Supplier,bool SupplierActive)
{
    public bool Low => Active && Quantity<=Minimum;
    public string Label => $"{Name} (#{Id})";
}
public sealed record WorkshopMove(int Id,int PartId,byte Kind,int Qty,int Before,int After,DateTime AtUtc,int? OrderId,
    int? SupplierId,string Supplier,bool SupplierActive,int ActorId,string Actor)
{
    public static string KindText(int kind) => kind switch { 1=>"رصيد افتتاحي",2=>"توريد للورشة",3=>"صرف للأمر",4=>"إرجاع من أمر",5=>"تسوية زيادة",6=>"تسوية نقص",_=>"غير معروف" };
}
public enum ServiceReportMode { Received, Movement, Delivered, Stock }
public sealed record MaintenanceReportData(ReportPeriod Period, DateTime ReadAtUtc, IReadOnlyDictionary<int,ServiceTicket> Orders,
    IReadOnlyList<ServiceLine> Lines,IReadOnlyList<ServicePayment> Payments,IReadOnlyList<ServiceActivity> Activities,
    IReadOnlyDictionary<int,WorkshopPart> Parts,IReadOnlyList<WorkshopMove> Moves);
public interface IMaintenanceReportSource : IReportSource<MaintenanceReportData> { }
