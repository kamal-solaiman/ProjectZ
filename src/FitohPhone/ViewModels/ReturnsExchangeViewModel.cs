using System.Collections.ObjectModel;
using System.Windows.Input;
using FitohPhone.Data;
using FitohPhone.Helpers;
using FitohPhone.Models;

namespace FitohPhone.ViewModels;

public sealed class ReturnsExchangeViewModel : ObservableObject
{
    private readonly CustomerRepository _customerRepo;
    private readonly ProductRepository _productRepo;
    private readonly SalesInvoiceRepository _invoiceRepo;
    private readonly ReturnExchangeRepository _returnRepo;
    private readonly int _userId;

    public ReturnsExchangeViewModel(CustomerRepository customerRepo, ProductRepository productRepo,
        SalesInvoiceRepository invoiceRepo, ReturnExchangeRepository returnRepo, int userId)
    {
        _customerRepo = customerRepo;
        _productRepo = productRepo;
        _invoiceRepo = invoiceRepo;
        _returnRepo = returnRepo;
        _userId = userId;
        LoadCommand = new AsyncCommand(LoadAsync);
    }

    public AsyncCommand LoadCommand { get; }

    public ObservableCollection<ReturnExchange> Returns { get; } = new();

    private int _totalReturns; public int TotalReturns { get => _totalReturns; set { _totalReturns = value; Changed(); } }
    private decimal _totalRefunded; public decimal TotalRefunded { get => _totalRefunded; set { _totalRefunded = value; Changed(); } }

    private bool _busy;
    public bool IsBusy { get => _busy; private set { _busy = value; Changed(); Changed(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Returns.Clear();
            foreach (var ret in await _returnRepo.GetAllAsync(System.Threading.CancellationToken.None))
                Returns.Add(ret);

            TotalReturns = Returns.Count;
            TotalRefunded = Returns.Sum(r => r.RefundAmount);
        }
        finally { IsBusy = false; }
    }

    public async Task<int> CreateReturnAsync(ReturnExchange ret, CancellationToken ct)
    {
        return await _returnRepo.CreateAsync(ret, ct);
    }
}
