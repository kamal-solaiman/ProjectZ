using System.Collections.ObjectModel;
using System.Windows.Input;
using FitohPhone.Data;
using FitohPhone.Helpers;
using FitohPhone.Models;

namespace FitohPhone.ViewModels;

public sealed class InvoicesLogViewModel : ObservableObject
{
    private readonly SalesInvoiceRepository _invoiceRepo;
    private readonly int _userId;

    public InvoicesLogViewModel(SalesInvoiceRepository invoiceRepo, int userId)
    {
        _invoiceRepo = invoiceRepo;
        _userId = userId;
        LoadCommand = new AsyncCommand(LoadAsync);
    }

    public AsyncCommand LoadCommand { get; }

    public ObservableCollection<SalesInvoiceListItem> Invoices { get; } = new();

    private int _totalInvoices; public int TotalInvoices { get => _totalInvoices; set { _totalInvoices = value; Changed(); } }
    private decimal _totalSales; public decimal TotalSales { get => _totalSales; set { _totalSales = value; Changed(); } }
    private decimal _totalPaid; public decimal TotalPaid { get => _totalPaid; set { _totalPaid = value; Changed(); } }
    private decimal _totalRemaining; public decimal TotalRemaining { get => _totalRemaining; set { _totalRemaining = value; Changed(); } }

    // تفاصيل الفاتورة المختارة للعرض في اللوحة السفلية
    private string? _selectedInvoiceItems;
    public string? SelectedInvoiceItems { get => _selectedInvoiceItems; private set { _selectedInvoiceItems = value; Changed(); } }

    private string? _selectedInvoicePayments;
    public string? SelectedInvoicePayments { get => _selectedInvoicePayments; private set { _selectedInvoicePayments = value; Changed(); } }

    private bool _busy;
    public bool IsBusy { get => _busy; private set { _busy = value; Changed(); Changed(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Invoices.Clear();
            foreach (var inv in await _invoiceRepo.GetListAsync(System.Threading.CancellationToken.None))
                Invoices.Add(inv);

            TotalInvoices = Invoices.Count;
            TotalSales = Invoices.Sum(i => i.GrandTotal);
            TotalPaid = Invoices.Sum(i => i.PaidAmount);
            TotalRemaining = Invoices.Sum(i => i.RemainingAmount);
        }
        finally { IsBusy = false; }
    }

    public async Task<bool> CancelInvoiceAsync(int invoiceId, CancellationToken ct)
    {
        return await _invoiceRepo.CancelAsync(invoiceId, _userId, ct);
    }

    /// <summary>
    /// تحميل تفاصيل الفاتورة المختارة للعرض في اللوحة السفلية
    /// </summary>
    public async Task LoadInvoiceDetailsAsync(int invoiceId)
    {
        var invoice = await _invoiceRepo.GetByIdAsync(invoiceId, System.Threading.CancellationToken.None);
        if (invoice is null) return;

        SelectedInvoiceItems = string.Join(" | ", invoice.Items.Select(i => $"{i.Quantity}× {i.ProductName} ({i.PriceTierUsed}: {i.UnitPrice:N0} ج)"));
        SelectedInvoicePayments = string.Join(" | ", invoice.Payments.Select(p => $"{p.PaymentMethodAr}: {p.Amount:N0} ج"));
    }
}
