namespace ProjectZ.Models;

// A different unit of analysis in each mode: account, ledger leg, lifecycle event, transfer, or wallet limit.
public enum FinanceReportMode { Statement, Ledger, FeeEvents, IssuedTransfers, Limits }
public enum FinanceSource { Independent, Sale, ReturnExchange, Purchase, Service, Expense, CustomerTransfer, TransferGroup, Other }
public enum FinanceDestination { Account, Sale, ReturnExchange, Purchase, Service, Expense, VodafoneRecord, InstaPayRecord }
public sealed record FinanceLink(FinanceDestination Destination,int Id);

public sealed record BookAccount(int Id, byte Provider, string SafeAlias, string MaskedIdentifier, bool Active, bool IsDefault,
    DateTime CreatedUtc, DateTime UpdatedUtc, decimal Current, decimal DayInLimit, decimal DayOutLimit, decimal MonthInLimit,
    decimal MonthOutLimit, decimal? PreviousClose, decimal? NextOpen, decimal? LatestPostedBalance)
{
    public string ProviderLabel => Provider switch {0=>"الخزنة",1=>"فودافون كاش",2=>"إنستاباي",_=>"حساب"};
    public string Label => $"{ProviderLabel} · {SafeAlias} (#{Id})";
    public FinanceLink? Link => Active ? new(FinanceDestination.Account,Id) : null;
}
public sealed record BookLeg(int Id,int AccountId,DateTime AtUtc,bool Incoming,decimal Amount,decimal After,bool Counts,int ActorId,
    FinanceSource Source,int? SourceId,bool HasDocument,string Document,string Purpose,Guid? TransferGroup)
{
    public decimal Delta=>Incoming?Amount:-Amount;
    public decimal Before=>After-Delta;
    public string Reference=>Document.Length>0?Document:SourceId is int id?$"#{id}":TransferGroup?.ToString()??"";
}
public sealed record FeeTransfer(int Id,int WalletId,int? CashId,byte Kind,decimal Principal,decimal Commission,decimal CashDelta,
    string MaskedCustomer,int ActorId,DateTime IssuedUtc,bool Reversed,DateTime? ReversedUtc,int? ReverserId,int? Predecessor,int? Replacement,
    bool ApplicationConfirmed,bool UndoConfirmed)
{
    public string Direction=>Kind==1?"إيداع للعميل":"سحب للعميل";
    public decimal? CurrentFee=>ApplicationConfirmed && (!Reversed || UndoConfirmed) ? (Reversed?0m:Commission) : null;
}
public sealed record LimitUse(int AccountId,bool Incoming,decimal Today,decimal Month);
public sealed record FinanceReportData(ReportPeriod Period,DateTime ReadAtUtc,IReadOnlyDictionary<int,BookAccount> Accounts,IReadOnlyList<BookLeg> Legs,
    IReadOnlyDictionary<int,FeeTransfer> Transfers,IReadOnlyList<LimitUse> Usage,IReadOnlyDictionary<int,string> Actors)
{
    public string Actor(int id)=>Actors.TryGetValue(id,out var name)?$"{name} (#{id})":$"مستخدم #{id}";
    public FinanceLink? TransferLink(FeeTransfer t)=>Accounts[t.WalletId].Provider switch {
        1=>new(FinanceDestination.VodafoneRecord,t.Id),2=>new(FinanceDestination.InstaPayRecord,t.Id),_=>null };
    public FinanceLink? SourceLink(BookLeg leg)
    {
        if(!leg.HasDocument || leg.SourceId is not int id)return null;
        return leg.Source switch {
            FinanceSource.Sale=>new(FinanceDestination.Sale,id),FinanceSource.ReturnExchange=>new(FinanceDestination.ReturnExchange,id),
            FinanceSource.Purchase=>new(FinanceDestination.Purchase,id),FinanceSource.Service=>new(FinanceDestination.Service,id),
            FinanceSource.Expense=>new(FinanceDestination.Expense,id),FinanceSource.CustomerTransfer when Transfers.TryGetValue(id,out var t)=>TransferLink(t),_=>null };
    }
}
public interface IFinanceReportSource : IReportSource<FinanceReportData> { }
