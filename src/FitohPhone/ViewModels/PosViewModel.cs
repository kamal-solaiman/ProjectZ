using System.Collections.ObjectModel;
using System.Windows.Input;
using FitohPhone.Data;
using FitohPhone.Helpers;
using FitohPhone.Models;

namespace FitohPhone.ViewModels;

public sealed class PosViewModel : ObservableObject
{
    private readonly ProductRepository _productRepo;
    private readonly CustomerRepository _customerRepo;
    private readonly SalesInvoiceRepository _invoiceRepo;
    private readonly int _userId;

    public PosViewModel(ProductRepository productRepo, CustomerRepository customerRepo,
        SalesInvoiceRepository invoiceRepo, int userId)
    {
        _productRepo = productRepo;
        _customerRepo = customerRepo;
        _invoiceRepo = invoiceRepo;
        _userId = userId;

        AddItemCommand = new RelayCommand(AddItem);
        RemoveItemCommand = new RelayCommand<int>(RemoveItem);
        SaveInvoiceCommand = new AsyncCommand(SaveInvoiceAsync);
        NewInvoiceCommand = new RelayCommand(NewInvoice);
    }

    public RelayCommand AddItemCommand { get; }
    public RelayCommand<int> RemoveItemCommand { get; }
    public AsyncCommand SaveInvoiceCommand { get; }
    public RelayCommand NewInvoiceCommand { get; }

    public ObservableCollection<PosInvoiceLine> Lines { get; } = new();

    private string _barcodeInput = "";
    public string BarcodeInput { get => _barcodeInput; set { _barcodeInput = value; Changed(); } }

    private int? _selectedCustomerId;
    public int? SelectedCustomerId
    {
        get => _selectedCustomerId;
        set { _selectedCustomerId = value; Changed(); Changed(nameof(HasCustomer)); }
    }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public bool HasCustomer => SelectedCustomerId.HasValue;

    private string _priceTier = "قطاعي";
    public string PriceTier { get => _priceTier; set { _priceTier = value; Changed(); UpdateLinePrices(); } }

    private decimal _cashAmount; public decimal CashAmount { get => _cashAmount; set { _cashAmount = value; Changed(); Changed(nameof(PaymentTotal)); Changed(nameof(RemainingToPay)); } }
    private decimal _vodafoneAmount; public decimal VodafoneAmount { get => _vodafoneAmount; set { _vodafoneAmount = value; Changed(); Changed(nameof(PaymentTotal)); Changed(nameof(RemainingToPay)); } }
    private decimal _instaPayAmount; public decimal InstaPayAmount { get => _instaPayAmount; set { _instaPayAmount = value; Changed(); Changed(nameof(PaymentTotal)); Changed(nameof(RemainingToPay)); } }

    public decimal PaymentTotal => CashAmount + VodafoneAmount + InstaPayAmount;
    public decimal InvoiceTotal => Lines.Sum(l => l.LineTotal);
    public decimal RemainingToPay => Math.Max(0, InvoiceTotal - PaymentTotal);

    private bool _busy;
    public bool IsBusy { get => _busy; private set { _busy = value; Changed(); Changed(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;

    public string[] PriceTiers => ["قطاعي", "مميز", "جملة"];
    public string[] PaymentMethods => ["نقدي", "فودافون كاش", "إنستاباي"];

    private void AddItem()
    {
        if (string.IsNullOrWhiteSpace(BarcodeInput)) return;
        var product = _productRepo.GetByBarcodeAsync(BarcodeInput, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
        if (product is null)
        {
            // يمكن إضافة رسالة خطأ هنا
            return;
        }
        var existing = Lines.FirstOrDefault(l => l.ProductId == product.Id);
        if (existing != null)
        {
            existing.Quantity++;
            existing.LineTotal = existing.Quantity * existing.UnitPrice;
        }
        else
        {
            var price = PriceTier switch
            {
                "جملة" => product.PriceWholesale,
                "مميز" => product.PriceSpecial,
                _ => product.PriceRetail
            };
            Lines.Add(new PosInvoiceLine(product.Id, product.Barcode, product.NameAr, 1, price, price));
        }
        BarcodeInput = "";
        Changed(nameof(InvoiceTotal));
        Changed(nameof(RemainingToPay));
    }

    private void RemoveItem(int lineId)
    {
        var line = Lines.FirstOrDefault(l => l.Id == lineId);
        if (line != null) Lines.Remove(line);
        Changed(nameof(InvoiceTotal));
        Changed(nameof(RemainingToPay));
    }

    private void UpdateLinePrices()
    {
        foreach (var line in Lines)
        {
            // إعادة حساب السعر حسب السعر المحدد للفاتورة (يمكن تخصيص لكل صنف على حدة)
        }
        Changed(nameof(InvoiceTotal));
        Changed(nameof(RemainingToPay));
    }

    private void NewInvoice()
    {
        Lines.Clear();
        SelectedCustomerId = null;
        CustomerName = null;
        CustomerPhone = null;
        PriceTier = "قطاعي";
        CashAmount = 0;
        VodafoneAmount = 0;
        InstaPayAmount = 0;
        Changed(nameof(InvoiceTotal));
        Changed(nameof(RemainingToPay));
    }

    private async Task SaveInvoiceAsync()
    {
        if (Lines.Count == 0) return;
        IsBusy = true;
        try
        {
            var invoice = new SalesInvoice(
                0, $"POS-{DateTime.Now:yyyyMMddHHmmss}", DateTime.Now,
                SelectedCustomerId, CustomerName, CustomerPhone,
                _userId, "المستخدم", InvoiceTotal, 0, 0, InvoiceTotal,
                PaymentTotal, Math.Max(0, InvoiceTotal - PaymentTotal),
                PaymentTotal >= InvoiceTotal ? "مدفوع" : "جزئي", PriceTier, null,
                false, null, null, DateTime.Now,
                Lines.Select(l => new SalesInvoiceItem(
                    0, 0, l.ProductId, l.Barcode, l.Name, l.Quantity, l.UnitPrice, 0, l.LineTotal, PriceTier)).ToList(),
                new List<SalesInvoicePayment>
                {
                    new(0, 0, "Cash", "نقدي", CashAmount, null, DateTime.Now),
                    new(0, 0, "VodafoneCash", "فودافون كاش", VodafoneAmount, null, DateTime.Now),
                    new(0, 0, "InstaPay", "إنستاباي", InstaPayAmount, null, DateTime.Now),
                }.Where(p => p.Amount > 0).ToList());

            var id = await _invoiceRepo.CreateAsync(invoice, System.Threading.CancellationToken.None);

            // تحديث مخزون الأصناف المباعة
            foreach (var line in Lines)
                await _productRepo.UpdateStockAsync(line.ProductId, -line.Quantity, System.Threading.CancellationToken.None);

            NewInvoice();
        }
        finally { IsBusy = false; }
    }
}

public sealed record PosInvoiceLine(
    int Id, string Barcode, string Name, int Quantity, decimal UnitPrice, decimal LineTotal)
{
    public int ProductId { get; init; }
}
