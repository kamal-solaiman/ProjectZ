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
        LoadCommand = new AsyncCommand(LoadAsync);
    }

    public RelayCommand AddItemCommand { get; }
    public RelayCommand<int> RemoveItemCommand { get; }
    public AsyncCommand SaveInvoiceCommand { get; }
    public RelayCommand NewInvoiceCommand { get; }
    public AsyncCommand LoadCommand { get; }

    public ObservableCollection<PosInvoiceLine> Lines { get; } = new();
    public ObservableCollection<Product> Products { get; } = new();
    public ObservableCollection<Customer> Customers { get; } = new();

    private string _barcodeInput = "";
    public string BarcodeInput { get => _barcodeInput; set { _barcodeInput = value; Changed(); } }

    private int? _selectedCustomerId;
    public int? SelectedCustomerId
    {
        get => _selectedCustomerId;
        set { _selectedCustomerId = value; Changed(); Changed(nameof(HasCustomer)); Changed(nameof(SelectedCustomerName)); Changed(nameof(SelectedCustomerPhone)); }
    }
    public string? SelectedCustomerName => SelectedCustomerId.HasValue ? Customers.FirstOrDefault(c => c.Id == _selectedCustomerId)?.Name : null;
    public string? SelectedCustomerPhone => SelectedCustomerId.HasValue ? Customers.FirstOrDefault(c => c.Id == _selectedCustomerId)?.Phone : null;

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

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Products.Clear();
            Customers.Clear();

            foreach (var p in await _productRepo.GetAllAsync(System.Threading.CancellationToken.None))
                Products.Add(p);

            foreach (var c in await _customerRepo.GetAllAsync(System.Threading.CancellationToken.None))
                Customers.Add(c);
        }
        finally { IsBusy = false; }
    }

    private void AddItem()
    {
        if (string.IsNullOrWhiteSpace(BarcodeInput)) return;

        // البحث بالباركود أو بالاسم
        var product = Products.FirstOrDefault(p => p.Barcode == BarcodeInput ||
            p.NameAr.Contains(BarcodeInput, System.StringComparison.OrdinalIgnoreCase));

        if (product is null)
        {
            // البحث غير المتزامن كاحتياطي
            var task = _productRepo.GetByBarcodeAsync(BarcodeInput, System.Threading.CancellationToken.None);
            product = task.GetAwaiter().GetResult();
        }

        if (product is null) return;

        var existing = Lines.FirstOrDefault(l => l.ProductId == product.Id);
        if (existing != null)
        {
            var updatedLine = new PosInvoiceLine(
                existing.Id, existing.Barcode, existing.Name, existing.Quantity + 1, existing.UnitPrice, (existing.Quantity + 1) * existing.UnitPrice)
            { ProductId = existing.ProductId };
            Lines.Remove(existing);
            Lines.Add(updatedLine);
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
                SelectedCustomerId, SelectedCustomerName, SelectedCustomerPhone,
                _userId, "المستخدم", InvoiceTotal, 0, 0, InvoiceTotal,
                PaymentTotal, Math.Max(0, InvoiceTotal - PaymentTotal),
                PaymentTotal >= InvoiceTotal ? "مدفوع" : "جزئي", PriceTier, null,
                false, null, DateTime.Now,
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
