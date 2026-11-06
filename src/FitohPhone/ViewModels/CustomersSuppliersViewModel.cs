using System.Collections.ObjectModel;
using System.Windows.Input;
using FitohPhone.Data;
using FitohPhone.Helpers;
using FitohPhone.Models;

namespace FitohPhone.ViewModels;

public sealed class CustomersSuppliersViewModel : ObservableObject
{
    private readonly CustomerRepository _customerRepo;
    private readonly SupplierRepository _supplierRepo;
    private readonly SalesInvoiceRepository _invoiceRepo;
    private readonly PhoneRepairRepository _repairRepo;
    private readonly TransferRepository _transferRepo;
    private readonly int _userId;

    public CustomersSuppliersViewModel(CustomerRepository customerRepo, SupplierRepository supplierRepo, int userId)
    {
        _customerRepo = customerRepo;
        _supplierRepo = supplierRepo;
        _userId = userId;
        LoadCommand = new AsyncCommand(LoadAsync);
    }

    public AsyncCommand LoadCommand { get; }

    public ObservableCollection<Customer> Customers { get; } = new();
    public ObservableCollection<Supplier> Suppliers { get; } = new();

    // تفاصيل العميل المحدد في لوحة اليمين
    private string? _selectedCustomerName;
    public string? SelectedCustomerName { get => _selectedCustomerName; private set { _selectedCustomerName = value; Changed(); } }

    private string? _selectedCustomerPhone;
    public string? SelectedCustomerPhone { get => _selectedCustomerPhone; private set { _selectedCustomerPhone = value; Changed(); } }

    private string? _selectedSupplierName;
    public string? SelectedSupplierName { get => _selectedSupplierName; set { _selectedSupplierName = value; Changed(); } }

    private string? _selectedSupplierPhone;
    public string? SelectedSupplierPhone { get => _selectedSupplierPhone; set { _selectedSupplierPhone = value; Changed(); } }

    private decimal _selectedCustomerTotal;
    public decimal SelectedCustomerTotal { get => _selectedCustomerTotal; private set { _selectedCustomerTotal = value; Changed(); } }

    private decimal _selectedCustomerBalance;
    public decimal SelectedCustomerBalance { get => _selectedCustomerBalance; private set { _selectedCustomerBalance = value; Changed(); } }

    public string StatementSummary =>
        $"إجمالي المسحوبات: {SelectedCustomerTotal:N0} ج.م  |  الرصيد الحالي: {SelectedCustomerBalance:N0} ج.م";

    // مجموعات التقارير للعميل المحدد
    public ObservableCollection<CustomerInvoiceItem> CustomerInvoices { get; } = new();
    public ObservableCollection<CustomerRepairItem> CustomerRepairs { get; } = new();
    public ObservableCollection<CustomerTransferItem> CustomerTransfers { get; } = new();

    private int _totalCustomers; public int TotalCustomers { get => _totalCustomers; set { _totalCustomers = value; Changed(); } }
    private int _totalSuppliers; public int TotalSuppliers { get => _totalSuppliers; set { _totalSuppliers = value; Changed(); } }
    private decimal _totalCustomerOutstanding; public decimal TotalCustomerOutstanding { get => _totalCustomerOutstanding; set { _totalCustomerOutstanding = value; Changed(); } }
    private decimal _totalSupplierOutstanding; public decimal TotalSupplierOutstanding { get => _totalSupplierOutstanding; set { _totalSupplierOutstanding = value; Changed(); } }

    private bool _busy;
    public bool IsBusy { get => _busy; private set { _busy = value; Changed(); Changed(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Customers.Clear();
            Suppliers.Clear();

            foreach (var c in await _customerRepo.GetAllAsync(System.Threading.CancellationToken.None))
                Customers.Add(c);
            foreach (var s in await _supplierRepo.GetAllAsync(System.Threading.CancellationToken.None))
                Suppliers.Add(s);

            TotalCustomers = Customers.Count;
            TotalSuppliers = Suppliers.Count;
            TotalCustomerOutstanding = Customers.Sum(c => c.OutstandingBalance);
            TotalSupplierOutstanding = Suppliers.Sum(s => s.OutstandingBalance);

            // إذا كان هناك عميل محدد سابقاً، تحميل بياناته
            if (!string.IsNullOrEmpty(SelectedCustomerName))
            {
                await LoadCustomerDetailsAsync(SelectedCustomerName);
            }
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// تحميل تفاصيل العميل المحدد في اللوحة اليمنى (فواتير، صيانات، تحويلات)
    /// </summary>
    public async Task LoadCustomerDetailsAsync(string customerName)
    {
        SelectedCustomerName = customerName;
        var customer = Customers.FirstOrDefault(c => c.Name == customerName);
        if (customer == null) return;

        SelectedCustomerPhone = customer.Phone;
        SelectedCustomerTotal = customer.TotalPurchases;
        SelectedCustomerBalance = customer.OutstandingBalance;

        // تحميل فواتير العميل
        var allInvoices = await _invoiceRepo.GetListAsync(System.Threading.CancellationToken.None);
        CustomerInvoices.Clear();
        foreach (var inv in allInvoices.Where(i => i.CustomerName == customerName).Take(10))
        {
            CustomerInvoices.Add(new CustomerInvoiceItem(
                inv.InvoiceNumber, inv.InvoiceDate, $"{inv.CustomerName} - فاتورة مبيعات",
                inv.GrandTotal, inv.PaymentStatus));
        }

        // تحميل صيانات العميل
        var allRepairs = await _repairRepo.GetAllAsync(System.Threading.CancellationToken.None);
        CustomerRepairs.Clear();
        foreach (var rep in allRepairs.Where(r => r.CustomerName == customerName).Take(10))
        {
            CustomerRepairs.Add(new CustomerRepairItem(
                rep.RepairNumber, rep.ReceivedDate, $"{rep.DeviceModel} - {rep.ProblemDescription}",
                rep.TotalCost, rep.Status));
        }

        // تحميل تحركات العميل من التحويلات
        var allTransfers = await _transferRepo.GetTransactionsAsync(System.Threading.CancellationToken.None);
        CustomerTransfers.Clear();
        foreach (var tr in allTransfers.Where(t => t.UserName.Contains(customerName) || t.Notes?.Contains(customerName) == true).Take(10))
        {
            CustomerTransfers.Add(new CustomerTransferItem(
                tr.TransferDate, tr.TransferTypeAr, $"{tr.FromPhoneNumber} → {tr.ToPhoneNumber}",
                tr.Amount));
        }

        Changed(nameof(StatementSummary));
    }

    public async Task<int> AddCustomerAsync(string name, string phone, string? altPhone, string? address, string? city, string? email, CancellationToken ct)
    {
        return await _customerRepo.AddAsync(new Customer(
            0, name, phone, altPhone, address, city, email, 0, 0, 0, null, true, DateTime.Now, null), ct);
    }

    public async Task<bool> UpdateCustomerAsync(Customer customer, CancellationToken ct)
    {
        return await _customerRepo.UpdateAsync(customer, ct);
    }

    public async Task<bool> DeleteCustomerAsync(int id, CancellationToken ct)
    {
        return await _customerRepo.DeleteAsync(id, ct);
    }

    public async Task<int> AddSupplierAsync(string name, string phone, string? contact, string? address, string? city, string? email, CancellationToken ct)
    {
        return await _supplierRepo.AddAsync(new Supplier(
            0, name, phone, contact, address, city, email, 0, 0, 0, true, DateTime.Now, null), ct);
    }

    public async Task<bool> UpdateSupplierAsync(Supplier supplier, CancellationToken ct)
    {
        return await _supplierRepo.UpdateAsync(supplier, ct);
    }

    public async Task<bool> DeleteSupplierAsync(int id, CancellationToken ct)
    {
        return await _supplierRepo.DeleteAsync(id, ct);
    }
}

// عناصر عرض للتفاصيل في اللوحة اليمنى
public sealed record CustomerInvoiceItem(
    string InvoiceNumber,
    DateTime InvoiceDate,
    string Description,
    decimal Amount,
    string PaymentStatus);

public sealed record CustomerRepairItem(
    string RepairNumber,
    DateTime Date,
    string Description,
    decimal Cost,
    string Status);

public sealed record CustomerTransferItem(
    DateTime Date,
    string Provider,
    string Description,
    decimal Amount);
