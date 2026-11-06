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
        LoadCommand = new AsyncCommand(LoadAsync);
    }

    public RelayCommand AddItemCommand { get; }
    public RelayCommand<int> RemoveItemCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand LoadCommand { get; }

    public ObservableCollection<Supplier> Suppliers { get; } = new();
    public ObservableCollection<Product> Products { get; } = new();
    public ObservableCollection<PurchaseLine> Lines { get; } = new();

    private int? _selectedSupplierId;
    public int? SelectedSupplierId { get => _selectedSupplierId; set { _selectedSupplierId = value; Changed(); Changed(nameof(SelectedSupplierName)); } }
    public string? SelectedSupplierName => SelectedSupplierId.HasValue ? Suppliers.FirstOrDefault(s => s.Id == _selectedSupplierId)?.Name : null;

    private string _invoiceNumber = "";
    public string InvoiceNumber { get => _invoiceNumber; set { _invoiceNumber = value; Changed(); } }

    private decimal _cashPayment;
    public decimal CashPayment { get => _cashPayment; set { _cashPayment = value; Changed(); Changed(nameof(TotalPaid)); Changed(nameof(Remaining)); } }
    private decimal _vodafonePayment;
    public decimal VodafonePayment { get => _vodafonePayment; set { _vodafonePayment = value; Changed(); Changed(nameof(TotalPaid)); Changed(nameof(Remaining)); } }
    private decimal _instaPayPayment;
    public decimal InstaPayPayment { get => _instaPayPayment; set { _instaPayPayment = value; Changed(); Changed(nameof(TotalPaid)); Changed(nameof(Remaining)); } }

    public decimal InvoiceTotal => Lines.Sum(l => l.LineTotal);
    public decimal TotalPaid => CashPayment + VodafonePayment + InstaPayPayment;
    public decimal Remaining => Math.Max(0, InvoiceTotal - TotalPaid);

    private bool _busy;
    public bool IsBusy { get => _busy; private set { _busy = value; Changed(); Changed(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;

    private string _error = "";
    public string Error { get => _error; private set { _error = value; Changed(); } }

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Suppliers.Clear();
            Products.Clear();

            foreach (var s in await _supplierRepo.GetAllAsync(System.Threading.CancellationToken.None))
                Suppliers.Add(s);

            foreach (var p in await _productRepo.GetAllAsync(System.Threading.CancellationToken.None))
                Products.Add(p);

            InvoiceNumber = $"SUP-{DateTime.Now:yyyyMMddHHmmss}";
        }
        finally { IsBusy = false; }
    }

    private void AddItem()
    {
        // يتم استدعاؤه من خلال اختيار منتج من القائمة في الشاشة
    }

    public void AddProductToLine(Product product, int quantity, decimal unitCost)
    {
        var existing = Lines.FirstOrDefault(l => l.ProductId == product.Id);
        if (existing != null)
        {
            var updatedLine = new PurchaseLine(
                existing.Id, existing.ProductId, existing.Barcode, existing.Name,
                existing.Quantity + quantity, existing.UnitCost, (existing.Quantity + quantity) * existing.UnitCost);
            Lines.Remove(existing);
            Lines.Add(updatedLine);
        }
        else
        {
            Lines.Add(new PurchaseLine(
                0, product.Id, product.Barcode, product.NameAr, quantity, unitCost, quantity * unitCost));
        }
        Changed(nameof(InvoiceTotal));
        Changed(nameof(Remaining));
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
            var totalPaid = CashPayment + VodafonePayment + InstaPayPayment;
            var credit = Math.Max(0, InvoiceTotal - totalPaid);

            var invoice = new PurchaseInvoice(
                0, InvoiceNumber, DateTime.Now, SelectedSupplierId.Value, SelectedSupplierName ?? "",
                _userId, "المستخدم", InvoiceTotal, InvoiceTotal, totalPaid, credit,
                totalPaid >= InvoiceTotal ? "مدفوع" : (totalPaid > 0 ? "جزئي" : "آجل"),
                null, false, null, DateTime.Now,
                Lines.Select(l => new PurchaseInvoiceItem(
                    0, 0, l.ProductId, l.Barcode, l.Name, l.Quantity, l.UnitCost, l.LineTotal)).ToList(),
                new List<PurchaseInvoicePayment>
                {
                    new(0, 0, "Cash", "نقدي", CashPayment, DateTime.Now),
                    new(0, 0, "VodafoneCash", "فودافون كاش", VodafonePayment, DateTime.Now),
                    new(0, 0, "InstaPay", "إنستاباي", InstaPayPayment, DateTime.Now),
                }.Where(p => p.Amount > 0).ToList());

            var id = await _invoiceRepo.CreateAsync(invoice, System.Threading.CancellationToken.None);

            // نجاح - يمكن مسح الفاتورة
            Lines.Clear();
            CashPayment = 0;
            VodafonePayment = 0;
            InstaPayPayment = 0;
            InvoiceNumber = $"SUP-{DateTime.Now:yyyyMMddHHmmss}";
            Changed(nameof(InvoiceTotal));
            Changed(nameof(TotalPaid));
            Changed(nameof(Remaining));
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally { IsBusy = false; }
    }
}

public sealed record PurchaseLine(int Id, int ProductId, string Barcode, string Name, int Quantity, decimal UnitCost, decimal LineTotal);
