using ProjectZ.Helpers;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.ViewModels;

public interface IReportWorkspace : System.ComponentModel.INotifyPropertyChanged
{
    ReportPage? Page { get; }
    ReportTable? SelectedTable { get; }
    DateTime? From { get; }
    DateTime? Through { get; }
    bool CanExport { get; }
    bool CanPrint { get; }
    string ExportMode { get; }
    string ExportPrefix { get; }
    ActionCommand DrillCommand { get; }
    Task PresetAsync(int days);
}

public sealed record ReportChoice(int? Id, string Label);

/// <summary>Common snapshot lifecycle, filters and navigation for document reports.</summary>
public abstract class ReportWorkspaceViewModel<T> : ObservableObject, IReportWorkspace where T : class
{
    protected static readonly ReportChoice All = new(null, "الكل");
    private static readonly ReportChoice EveryAccount = new(null, "كل الحسابات");
    private static readonly ReportChoice EverySupplier = new(null, "كل الموردين");
    protected abstract IReadOnlyList<ReportChoice> StatusChoices { get; }
    protected abstract IReadOnlyList<ReportChoice> EventChoices { get; }
    public abstract string SectionTitle { get; }
    public abstract string ExportPrefix { get; }
    public abstract string SearchLabel { get; }
    public abstract string IssuerLabel { get; }
    public abstract string Explanation { get; }
    protected abstract IEnumerable<ReportChoice> GetIssuers(T data);
    protected abstract IEnumerable<ReportChoice> GetAccounts(T data);
    protected virtual IEnumerable<ReportChoice> GetSuppliers(T data) => [];
    protected abstract ReportPage Project(T data, bool movement, ReportFilter filter, string currency);
    private readonly IReportSource<T>? source;
    private readonly Action<int>? openInvoice;
    private readonly Action<int>? openSupplier;
    private CancellationTokenSource? loading;
    private T? data;
    private ReportPage? page;
    private ReportTable? table;
    private ReportRow? row;
    private DateTime? from = ReportPeriod.Today, through = ReportPeriod.Today;
    private int modeId;
    protected int ModeId => modeId;
    public virtual IReadOnlyList<ReportChoice> Modes { get; } = [new(0,"فواتير الفترة"),new(1,"حركة الفترة")];
    public string CohortModeLabel => Modes.First(m => m.Id == 0).Label;
    public string MovementModeLabel => Modes.First(m => m.Id == 1).Label;
    public bool HasExtendedModes => Modes.Count>2;
    public virtual string ExportMode => IsMovement ? "Activity" : "Invoices";
    public virtual bool ShowOrderFilters => true;
    public virtual bool ShowIssuerFilter => ShowOrderFilters;
    public virtual IReadOnlyList<ReportChoice> AccountChoices => Accounts;
    public virtual object? ExtraTools => null;
    protected virtual bool ClearOnModeChange => false;
    protected virtual void ClearExtraFilters() { }
    protected virtual void SetExtraChoices(T result) { }
    protected virtual void NotifyRowActions() { }
    protected virtual bool CanOpenDetail(ReportRow r) => openInvoice is not null && r.CanOpenInvoice && r.InvoiceIds.Length==1;
    protected virtual void OpenDetail(ReportRow r) { if(r.CanOpenInvoice && r.InvoiceIds.Length==1)openInvoice?.Invoke(r.InvoiceIds[0]); }
    private string search = "", detail = "", status = "";
    private ReportChoice issuer = All, selectedKind = All, selectedAccount = EveryAccount, supplier = EverySupplier;
    private HashSet<int>? drill;
    private HashSet<ReportEventKey>? eventDrill;
    private string drillLabel = "";
    private string? drillTable;

    protected ReportWorkspaceViewModel(IReportSource<T>? source, Action<int>? openInvoice, Action<int>? openSupplier = null, int initialMode = 0)
    {
        modeId = initialMode;
        this.source = source;
        this.openInvoice = openInvoice;
        this.openSupplier = openSupplier;
        ApplyCommand = new AsyncCommand(RefreshAsync);
        DrillCommand = new ActionCommand(OpenRow, () => row is not null && (table?.Grouped == true || CanOpenDetail(row)));
        OpenSupplierCommand = new ActionCommand(() => openSupplier?.Invoke(row!.SupplierId!.Value), () => HasSupplierTools && row?.SupplierId is not null);
        OpenRelatedCommand = new ActionCommand(() => openInvoice?.Invoke(row!.RelatedInvoiceId!.Value), () => HasRelated);
        BackToResultsCommand = new ActionCommand(() => ResetDrill(drillTable), () => HasDrill);
        ClearCommand = new RelayCommand(ClearFilters);
    }

    public virtual bool HasSupplierTools => openSupplier is not null;
    public bool HasRelated => row?.RelatedInvoiceId is not null;
    public ActionCommand OpenSupplierCommand { get; }
    public ActionCommand OpenRelatedCommand { get; }
    public IReadOnlyList<ReportChoice> Suppliers { get; private set; } = [EverySupplier];
    public ReportChoice Supplier { get => supplier; set => ChangeChoice(ref supplier, value, nameof(Supplier)); }
    public AsyncCommand ApplyCommand { get; }
    public ActionCommand DrillCommand { get; }
    public ActionCommand BackToResultsCommand { get; }
    public RelayCommand ClearCommand { get; }
    public IReadOnlyList<ReportChoice> Issuers { get; private set; } = [All];
    public IReadOnlyList<ReportChoice> Accounts { get; private set; } = [EveryAccount];
    public virtual IReadOnlyList<ReportChoice> KindOptions => IsMovement ? EventChoices : StatusChoices;
    public ReportChoice SelectedKind { get => selectedKind; set => ChangeChoice(ref selectedKind, value, nameof(SelectedKind)); }
    public ReportChoice SelectedAccount { get => selectedAccount; set => ChangeChoice(ref selectedAccount, value, nameof(SelectedAccount)); }
    public ReportChoice Issuer { get => issuer; set => ChangeChoice(ref issuer, value, nameof(Issuer)); }
    public virtual string KindLabel => IsMovement ? "نوع الحركة" : "حالة الفاتورة";
    public virtual string AccountLabel => IsMovement ? "حركات الحساب الفعلي فقط" : "فواتير تعاملت مع الحساب (الفاتورة كاملة)";

    public string Search
    {
        get => search;
        set { if (search != value) { search = value ?? ""; Changed(); ResetDrill(); } }
    }
    public string Detail
    {
        get => detail;
        set { if (detail != value) { detail = value ?? ""; Changed(); ResetDrill(); } }
    }
    public DateTime? From
    {
        get => from;
        set { if (from != value) { from = value; Changed(); InvalidateDates(); } }
    }
    public DateTime? Through
    {
        get => through;
        set { if (through != value) { through = value; Changed(); InvalidateDates(); } }
    }
    public ReportChoice Mode
    {
        get => Modes.First(m=>m.Id==modeId);
        set
        {
            if(value?.Id is not int id || id==modeId || !Modes.Any(m=>m.Id==id))return;
            modeId=id; detail=""; selectedKind=All;
            Changed(); Changed(nameof(IsMovement)); Changed(nameof(IsCohort)); Changed(nameof(KindOptions)); Changed(nameof(SelectedKind));
            Changed(nameof(KindLabel)); Changed(nameof(AccountLabel)); Changed(nameof(DetailLabel)); Changed(nameof(SearchLabel)); Changed(nameof(IssuerLabel));
            Changed(nameof(AccountChoices)); Changed(nameof(ShowIssuerFilter)); Changed(nameof(Explanation)); Changed(nameof(Detail)); Changed(nameof(ShowOrderFilters)); Changed(nameof(HasSupplierTools));
            if(ClearOnModeChange)ClearFilters(); else ResetDrill();
        }
    }
    public bool IsMovement { get => modeId==1; set { if(value!=IsMovement) Mode=Modes.First(m=>m.Id==(value?1:0)); } }
    public bool IsCohort { get => !IsMovement; set { if(value) IsMovement=false; } }
    public virtual string DetailLabel => IsMovement ? "نوع الحركة، المنفذ، الحساب أو البيان" : "فواتير تتضمن صنفًا، تصنيفًا أو باركودًا";
    public string SourceLabel => source is null ? "معاينة • غير متصل" : "المصدر • SQL Server";
    public string Status { get => status; private set { status = value; Changed(); } }
    public bool IsBusy => loading is not null;
    public bool HasDrill => drill is not null;
    public ReportPage? Page => page;
    public bool CanExport => page?.Tables.Any(t => t.Rows.Count > 0) == true;
    public bool CanPrint => table?.Rows.Count > 0;
    public ReportTable? SelectedTable
    {
        get => table;
        set { table = value; SelectedRow = null; Changed(); Changed(nameof(CanPrint)); Changed(nameof(TableCaption)); }
    }
    public virtual string TableCaption => table is null ? "" : $"{table.Rows.Count:N0} صف • " + (table.Grouped
        ? (IsMovement ? "افتح المجموعة لعرض حركاتها" : "افتح المجموعة لعرض فواتيرها") : row is { CanOpenInvoice: false } ? "مستند محذوف • سجل تدقيق فقط" : "افتح الصف للانتقال إلى الفاتورة");
    public ReportRow? SelectedRow
    {
        get => row;
        set
        {
            row = value; NotifyRowActions(); Changed(); Changed(nameof(HasRelated)); Changed(nameof(TableCaption));
            DrillCommand.NotifyCanExecuteChanged(); OpenSupplierCommand.NotifyCanExecuteChanged(); OpenRelatedCommand.NotifyCanExecuteChanged();
        }
    }

    private void ChangeChoice(ref ReportChoice field, ReportChoice? value, string property)
    {
        // WPF momentarily clears SelectedItem when the choices are reloaded.
        if (value is null || value == field) return;
        field = value;
        Changed(property);
        ResetDrill();
    }
    private void ClearFilters()
    {
        ClearExtraFilters();
        search = detail = "";
        supplier = EverySupplier; Changed(nameof(Supplier));
        issuer = All;
        selectedKind = All;
        selectedAccount = EveryAccount;
        Changed(nameof(Search)); Changed(nameof(Detail)); Changed(nameof(Issuer));
        Changed(nameof(SelectedKind)); Changed(nameof(SelectedAccount));
        ResetDrill();
    }
    protected void ResetDrill(string? preferredTable = null)
    {
        drill = null;
        eventDrill = null;
        drillLabel = "";
        drillTable = null;
        Changed(nameof(HasDrill));
        BackToResultsCommand.NotifyCanExecuteChanged();
        Rebuild(preferredTable);
    }
    private void InvalidateDates()
    {
        Leave();
        Status = "تغيّرت الفترة. اضغط «تحميل الفترة» قبل قراءة النتائج أو تصديرها.";
    }
    public void Leave()
    {
        var previous = loading;
        loading = null;
        previous?.Cancel();
        Changed(nameof(IsBusy));
        data = null;
        ResetDrill();
    }
    public async Task RefreshAsync()
    {
        Leave();
        if (source is null)
        {
            Status = "لا يوجد اتصال في وضع المعاينة؛ لا تُعرض أرصدة تجريبية.";
            return;
        }
        using var request = new CancellationTokenSource();
        loading = request;
        Changed(nameof(IsBusy));
        try
        {
            var period = new ReportPeriod(From, Through);
            Status = "جارٍ تحميل بيانات التقرير من قاعدة البيانات…";
            var result = await source.LoadAsync(period, request.Token);
            if (request.IsCancellationRequested) return;
            SetChoices(result);
            data = result;
            Rebuild();
            Status = "تم تحميل البيانات • " + period.Label;
        }
        catch (Exception) when (request.IsCancellationRequested) { }
        catch (Exception ex)
        {
            data = null;
            Rebuild();
            Status = "تعذر عرض التقرير: " + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(loading, request)) { loading = null; Changed(nameof(IsBusy)); }
        }
    }
    private void SetChoices(T result)
    {
        // Preserve explicit filters even if a refreshed period no longer contains a match.
        static ReportChoice[] Options(IEnumerable<ReportChoice> choices, ReportChoice first, ReportChoice selected) =>
            choices.DistinctBy(c => c.Id).OrderBy(c => c.Label).Prepend(first).Append(selected).DistinctBy(c => c.Id).ToArray();
        SetExtraChoices(result);
        Issuers = Options(GetIssuers(result), All, issuer);
        Accounts = Options(GetAccounts(result), EveryAccount, selectedAccount);
        Suppliers = Options(GetSuppliers(result), EverySupplier, supplier);
        issuer = Issuers.First(c => c.Id == issuer.Id);
        selectedAccount = Accounts.First(c => c.Id == selectedAccount.Id);
        supplier = Suppliers.First(c => c.Id == supplier.Id);
        Changed(nameof(Issuers)); Changed(nameof(Issuer)); Changed(nameof(Accounts)); Changed(nameof(AccountChoices)); Changed(nameof(SelectedAccount));
        Changed(nameof(Suppliers)); Changed(nameof(Supplier));
    }
    public async Task PresetAsync(int days)
    {
        var today = ReportPeriod.Today;
        From = days == 0 ? new DateTime(today.Year, today.Month, 1) : today.AddDays(1 - days);
        Through = today;
        await RefreshAsync();
    }
    private void Rebuild(string? preferredTable = null)
    {
        var title = preferredTable ?? table?.Title;
        page = data is null ? null : Project(data, IsMovement,
            new(search, issuer.Id, detail, drill, drillLabel, selectedKind.Id, selectedAccount.Id, eventDrill, supplier.Id, drillTable),
            StoreIdentity.Current.CurrencyLabel);
        Changed(nameof(Page));
        SelectedTable = page?.Tables.FirstOrDefault(t => t.Title == title) ?? page?.Tables.FirstOrDefault();
        Changed(nameof(CanExport));
    }
    private void OpenRow()
    {
        if (row is null || table is null) return;
        if (table.Grouped)
        {
            drill = row.InvoiceIds.ToHashSet();
            eventDrill = row.EventKeys?.ToHashSet(); // Some document groups drill to exact allocated shares, not whole invoices.
            drillLabel = row.Display[0];
            drillTable = table.Title;
            Changed(nameof(HasDrill));
            BackToResultsCommand.NotifyCanExecuteChanged();
            Rebuild(page?.Tables[0].Title);
        }
        else if (CanOpenDetail(row)) OpenDetail(row);
    }
}
