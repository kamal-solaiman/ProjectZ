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
        }
        finally { IsBusy = false; }
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
