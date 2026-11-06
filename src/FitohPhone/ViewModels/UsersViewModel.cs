using System.Collections.ObjectModel;
using System.Windows.Input;
using FitohPhone.Data;
using FitohPhone.Helpers;
using FitohPhone.Models;

namespace FitohPhone.ViewModels;

public sealed class UsersViewModel : ObservableObject
{
    private readonly UserRepository _userRepo;
    private readonly UserRoleRepository _roleRepo;
    private readonly int _currentUserId;

    public UsersViewModel(UserRepository userRepo, UserRoleRepository roleRepo, int currentUserId)
    {
        _userRepo = userRepo;
        _roleRepo = roleRepo;
        _currentUserId = currentUserId;
        LoadCommand = new AsyncCommand(LoadAsync);
        SavePermissionsCommand = new AsyncCommand(SavePermissionsAsync);
    }

    public AsyncCommand LoadCommand { get; }
    public AsyncCommand SavePermissionsCommand { get; }

    public ObservableCollection<UserSession> Users { get; } = new();
    public ObservableCollection<UserRole> Roles { get; } = new();
    public ObservableCollection<UserPermission> Permissions { get; } = new();

    private int _selectedUserId; public int SelectedUserId { get => _selectedUserId; set { _selectedUserId = value; Changed(); } }
    private string _selectedUserName = ""; public string SelectedUserName { get => _selectedUserName; set { _selectedUserName = value; Changed(); } }

    private int _totalUsers; public int TotalUsers { get => _totalUsers; set { _totalUsers = value; Changed(); } }
    private int _activeUsers; public int ActiveUsers { get => _activeUsers; set { _activeUsers = value; Changed(); } }

    private bool _busy;
    public bool IsBusy { get => _busy; private set { _busy = value; Changed(); Changed(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Users.Clear();
            Roles.Clear();
            Permissions.Clear();

            foreach (var u in await _userRepo.GetAllUsersAsync(System.Threading.CancellationToken.None))
                Users.Add(u);
            foreach (var r in await _roleRepo.GetAllAsync(System.Threading.CancellationToken.None))
                Roles.Add(r);

            TotalUsers = Users.Count;
            ActiveUsers = Users.Count; // جميع المستخدمين نشطين في هذه المرحلة
        }
        finally { IsBusy = false; }
    }

    public async Task<IReadOnlyList<UserPermission>> LoadPermissionsForUserAsync(int userId, CancellationToken ct)
    {
        var list = await _roleRepo.GetPermissionsForUserAsync(userId, ct);
        Permissions.Clear();
        foreach (var p in list) Permissions.Add(p);
        return list;
    }

    public async Task<bool> SavePermissionsAsync()
    {
        if (SelectedUserId <= 0) return false;
        var perms = Permissions.ToList();
        return await _roleRepo.SavePermissionsAsync(SelectedUserId, perms, System.Threading.CancellationToken.None);
    }
}
