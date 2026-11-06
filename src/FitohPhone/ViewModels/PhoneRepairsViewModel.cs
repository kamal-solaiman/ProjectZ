using System.Collections.ObjectModel;
using System.Windows.Input;
using FitohPhone.Data;
using FitohPhone.Helpers;
using FitohPhone.Models;

namespace FitohPhone.ViewModels;

public sealed class PhoneRepairsViewModel : ObservableObject
{
    private readonly PhoneRepairRepository _repairRepo;
    private readonly int _userId;

    public PhoneRepairsViewModel(PhoneRepairRepository repairRepo, int userId)
    {
        _repairRepo = repairRepo;
        _userId = userId;
        LoadCommand = new AsyncCommand(LoadAsync);
    }

    public AsyncCommand LoadCommand { get; }

    public ObservableCollection<PhoneRepair> Repairs { get; } = new();

    private int _totalRepairs; public int TotalRepairs { get => _totalRepairs; set { _totalRepairs = value; Changed(); } }
    private int _inProgress; public int InProgress { get => _inProgress; set { _inProgress = value; Changed(); } }
    private int _readyForDelivery; public int ReadyForDelivery { get => _readyForDelivery; set { _readyForDelivery = value; Changed(); } }
    private int _delivered; public int Delivered { get => _delivered; set { _delivered = value; Changed(); } }

    private bool _busy;
    public bool IsBusy { get => _busy; private set { _busy = value; Changed(); Changed(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Repairs.Clear();
            foreach (var r in await _repairRepo.GetAllAsync(System.Threading.CancellationToken.None))
                Repairs.Add(r);

            TotalRepairs = Repairs.Count;
            InProgress = Repairs.Count(r => r.Status == "قيد الصيانة");
            ReadyForDelivery = Repairs.Count(r => r.Status == "جاهز للتسليم");
            Delivered = Repairs.Count(r => r.Status == "تم التسليم");
        }
        finally { IsBusy = false; }
    }

    public async Task<int> AddRepairAsync(PhoneRepair repair, CancellationToken ct)
    {
        return await _repairRepo.AddAsync(repair, ct);
    }

    public async Task<bool> UpdateStatusAsync(int id, string status, int? deliveredByUserId, DateTime? actualDeliveryDate, CancellationToken ct)
    {
        return await _repairRepo.UpdateStatusAsync(id, status, deliveredByUserId, actualDeliveryDate, ct);
    }

    public async Task<bool> MarkBarcodePrintedAsync(int id, CancellationToken ct)
    {
        return await _repairRepo.MarkBarcodePrintedAsync(id, ct);
    }
}
