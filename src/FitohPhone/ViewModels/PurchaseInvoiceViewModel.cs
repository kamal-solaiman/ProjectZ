using System.Collections.ObjectModel;
using System.Windows.Input;
using FitohPhone.Data;
using FitohPhone.Helpers;
using FitohPhone.Models;

namespace FitohPhone.ViewModels;

public sealed class PurchaseInvoiceViewModel : ObservableObject
{
    private readonly SupplierRepository _supplierRepo;
    private readonly ProductRepository _productRepo;
    private readonly PurchaseInvoiceRepository _invoiceRepo;
    private readonly int _userId;

    public PurchaseInvoiceViewModel(SupplierRepository supplierRepo, ProductRepository productRepo,
        PurchaseInvoiceRepository invoiceRepo, int userId)
    {
        _supplierRepo = supplierRepo;
        _productRepo = productRepo;
        _invoiceRepo = invoiceRepo;
        _userId = userId;
        AddItemCommand = new RelayCommand(AddItem);
        RemoveItemCommand = new RelayCommand<int>(RemoveItem);
        SaveCommand = new AsyncCommand(SaveAsync);
    }

    public RelayCommand AddItemCommand { get; }
    public RelayCommand<int> RemoveItemCommand { get; }
    public AsyncCommand SaveCommand { get; }

    public ObservableCollection<PurchaseLine> Lines { get; } = new();

    private int? _selectedSupplierId;
    public int? SelectedSupplierId { get => _selectedSupplierId; set { _selectedSupplierId = value; Changed(); } }
    public string? SupplierName { get; set; }

    private string _invoiceNumber = "";
    public string InvoiceNumber { get => _invoiceNumber; set { _invoiceNumber = value; Changed(); } }

    private string _cashPayment = "";
    public string CashPayment { get => _cashPayment; set { _cashPayment = value; Changed(); } }
    private string _vodafonePayment = "";
    public string VodafonePayment { get => _vodafonePayment; set { _vodafonePayment = value; Changed(); } }
    private string _instaPayPayment = "";
    public string InstaPayPayment { get => _instaPayPayment; set { _instaPayPayment = value; Changed(); } }

    private decimal _creditRemaining; public decimal CreditRemaining { get => _creditRemaining; set { _creditRemaining = value; Changed(); } }

    public decimal InvoiceTotal => Lines.Sum(l => l.LineTotal);
    public decimal TotalPaid => ParseDecimal(CashPayment) + ParseDecimal(VodafonePayment) + ParseDecimal(InstaPayPayment);
    public decimal Remaining => Math.Max(0, InvoiceTotal - TotalPaid);

    private bool _busy;
    public bool IsBusy { get => _busy; private set { _busy = value; Changed(); Changed(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;

    private string _error = "";
    public string Error { get => _error; private set { _error = value; Changed(); } }

    private static decimal ParseDecimal(string? s) => decimal.TryParse(s, out var v) ? v : 0;

    private void AddItem()
    {
        // يُضاف من خلال اختيار منتج من القائمة في الشاشة
    }

    private void RemoveItem(int lineId)
    {
        var line = Lines.FirstOrDefault(l => l.Id == lineId);
        if (line != null) Lines.Remove(line);
        Changed(nameof(InvoiceTotal));
        Changed(nameof(Remaining));
    }

    private async Task SaveAsync()
    {
        Error = "";
        if (Lines.Count == 0)
        {
            Error = "يجب إضافة منتج واحد على الأقل.";
            return;
        }
        if (!SelectedSupplierId.HasValue)
        {
            Error = "يجب اختيار تاجر.";
            return;
        }

        IsBusy = true;
        try
        {
            var paidCash = ParseDecimal(CashPayment);
            var paidVoda = ParseDecimal(VodafonePayment);
            var paidInsta = ParseDecimal(InstaPayPayment);
            var totalPaid = paidCash + paidVoda + paidInsta;
            var credit = Math.Max(0, InvoiceTotal - totalPaid);

            var invoice = new PurchaseInvoice(
                0, InvoiceNumber, DateTime.Now, SelectedSupplierId.Value, SupplierName ?? "",
                _userId, "المستخدم", InvoiceTotal, InvoiceTotal, totalPaid, credit,
                totalPaid >= InvoiceTotal ? "مدفوع" : (totalPaid > 0 ? "جزئي" : "آجل"),
                null, false, null, null, DateTime.Now,
                Lines.Select(l => new PurchaseInvoiceItem(
                    0, 0, l.ProductId, l.Barcode, l.Name, l.Quantity, l.UnitCost, l.LineTotal)).ToList(),
                new List<PurchaseInvoicePayment>
                {
                    new(0, 0, "Cash", "نقدي", paidCash, DateTime.Now),
                    new(0, 0, "VodafoneCash", "فودافون كاش", paidVoda, DateTime.Now),
                    new(0, 0, "InstaPay", "إنستاباي", paidInsta, DateTime.Now),
                }.Where(p => p.Amount > 0).ToList());

            var id = await _invoiceRepo.CreateAsync(invoice, System.Threading.CancellationToken.None);
            // نجاح
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally { IsBusy = false; }
    }
}

public sealed record PurchaseLine(int Id, int ProductId, string Barcode, string Name, int Quantity, decimal UnitCost, decimal LineTotal);
