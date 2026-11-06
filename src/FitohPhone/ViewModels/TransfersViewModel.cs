using System.Collections.ObjectModel;
using System.Windows.Input;
using FitohPhone.Data;
using FitohPhone.Helpers;
using FitohPhone.Models;

namespace FitohPhone.ViewModels;

public sealed class TransfersViewModel : ObservableObject
{
    private readonly TransferRepository _transferRepo;
    private readonly int _userId;

    public TransfersViewModel(TransferRepository transferRepo, int userId)
    {
        _transferRepo = transferRepo;
        _userId = userId;
        LoadCommand = new AsyncCommand(LoadAsync);
    }

    public AsyncCommand LoadCommand { get; }

    public ObservableCollection<TransferLine> Lines { get; } = new();
    public ObservableCollection<TransferTransaction> Transactions { get; } = new();

    private int _totalLines; public int TotalLines { get => _totalLines; set { _totalLines = value; Changed(); } }
    private decimal _totalBalance; public decimal TotalBalance { get => _totalBalance; set { _totalBalance = value; Changed(); } }
    private int _totalTransactions; public int TotalTransactions { get => _totalTransactions; set { _totalTransactions = value; Changed(); } }

    private bool _busy;
    public bool IsBusy { get => _busy; private set { _busy = value; Changed(); Changed(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Lines.Clear();
            Transactions.Clear();

            foreach (var l in await _transferRepo.GetLinesAsync(System.Threading.CancellationToken.None))
                Lines.Add(l);
            foreach (var t in await _transferRepo.GetTransactionsAsync(System.Threading.CancellationToken.None))
                Transactions.Add(t);

            TotalLines = Lines.Count;
            TotalBalance = Lines.Sum(l => l.CurrentBalance);
            TotalTransactions = Transactions.Count;
        }
        finally { IsBusy = false; }
    }

    public async Task<int> AddLineAsync(string provider, string providerAr, string phoneNumber,
        decimal currentBalance, decimal dailyLimit, decimal monthlyLimit, CancellationToken ct)
    {
        return await _transferRepo.AddLineAsync(new TransferLine(
            0, provider, providerAr, phoneNumber, currentBalance, dailyLimit, monthlyLimit,
            0, 0, null, null, true, DateTime.Now, null), ct);
    }

    public async Task<bool> UpdateLineAsync(TransferLine line, CancellationToken ct)
    {
        return await _transferRepo.UpdateLineAsync(line, ct);
    }

    public async Task<bool> DeleteLineAsync(int id, CancellationToken ct)
    {
        return await _transferRepo.DeleteLineAsync(id, ct);
    }

    public async Task<int> CreateTransactionAsync(TransferTransaction txn, CancellationToken ct)
    {
        return await _transferRepo.CreateTransactionAsync(txn, ct);
    }
}
