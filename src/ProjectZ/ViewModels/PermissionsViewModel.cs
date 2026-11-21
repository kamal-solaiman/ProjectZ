using System.Collections.ObjectModel;
using ProjectZ.Data;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class PermissionEntryViewModel : ObservableObject
{
    private bool isGranted;
    public int Id { get; }
    public string Code { get; }
    public string Name { get; }
    public string Module { get; }
    public bool IsGranted { get => isGranted; set { if (isGranted == value) return; isGranted = value; Changed(); } }

    public PermissionEntryViewModel(PermissionCatalogRecord record, bool granted)
    {
        Id = record.Id;
        Code = record.Code;
        Name = record.Name;
        Module = record.Module;
        isGranted = granted;
    }
}

public sealed class PermissionGroupViewModel(string name, IEnumerable<PermissionEntryViewModel> permissions)
{
    public string Name { get; } = name;
    public ObservableCollection<PermissionEntryViewModel> Permissions { get; } = new(permissions);
}

public sealed class PermissionRoleOption(int id, string name)
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public override string ToString() => Name;
}

public sealed class PermissionRoleViewModel(SecurityRoleRecord record) : ObservableObject
{
    public int Id { get; } = record.Id;
    public string Name { get; } = record.Name;
    public string Description { get; } = record.Description;
    public bool IsSystem { get; } = record.IsSystem;
    public int AssignedUsers { get; } = record.AssignedUsers;
    public string Badge => IsSystem ? "دور أساسي" : "دور مخصص";
    public string AssignedUsersDisplay => $"مستخدمون مسندون: {AssignedUsers}";
}

public sealed class PermissionUserViewModel(SecurityUserRecord record) : ObservableObject
{
    public int Id { get; } = record.Id;
    public string Username { get; } = record.Username;
    public string DisplayName { get; } = record.DisplayName;
    public int? RoleId { get; } = record.RoleId;
    public string Display => $"{DisplayName} ({Username})";
}

/// <summary>Role editor and single-role assignment workspace.</summary>
public sealed class PermissionsViewModel : ObservableObject
{
    private readonly PermissionsRepository repository;
    private readonly UserSession actor;
    private PermissionRoleViewModel? selectedRole;
    private PermissionUserViewModel? selectedUser;
    private PermissionRoleOption? selectedUserRole;
    private string roleName = "";
    private string roleDescription = "";
    private string statusMessage = "";
    private bool isBusy;
    private bool isNewRole;

    public ObservableCollection<PermissionRoleViewModel> Roles { get; } = new();
    public ObservableCollection<PermissionUserViewModel> Users { get; } = new();
    public ObservableCollection<PermissionRoleOption> RoleOptions { get; } = new();
    public ObservableCollection<PermissionGroupViewModel> PermissionGroups { get; } = new();

    public PermissionRoleViewModel? SelectedRole
    {
        get => selectedRole;
        set
        {
            if (ReferenceEquals(selectedRole, value)) return;
            selectedRole = value;
            Changed();
            if (value is not null) LoadSelectedRolePermissions();
            else PermissionGroups.Clear();
            LoadRoleProperties(value);
            Changed(nameof(IsEditingSystemRole));
            Changed(nameof(CanDeleteRole));
            Changed(nameof(RoleModeTitle));
        }
    }

    public PermissionUserViewModel? SelectedUser
    {
        get => selectedUser;
        set
        {
            if (ReferenceEquals(selectedUser, value)) return;
            selectedUser = value;
            Changed();
            SelectedUserRole = value?.RoleId is int id ? RoleOptions.FirstOrDefault(r => r.Id == id) : RoleOptions.FirstOrDefault(r => r.Id == -1);
            Changed(nameof(IsAdminUserSelected));
            Changed(nameof(CanAssignSelectedUser));
        }
    }

    public PermissionRoleOption? SelectedUserRole
    {
        get => selectedUserRole;
        set { if (ReferenceEquals(selectedUserRole, value)) return; selectedUserRole = value; Changed(); }
    }

    public string RoleName { get => roleName; set { roleName = value; Changed(); } }
    public string RoleDescription { get => roleDescription; set { roleDescription = value; Changed(); } }
    public string StatusMessage { get => statusMessage; private set { statusMessage = value; Changed(); } }
    public bool IsBusy { get => isBusy; private set { isBusy = value; Changed(); } }
    public bool IsNewRole { get => isNewRole; private set { isNewRole = value; Changed(); Changed(nameof(RoleModeTitle)); Changed(nameof(IsEditingSystemRole)); } }
    public bool IsEditingSystemRole => !IsNewRole && SelectedRole?.IsSystem == true;
    public bool IsAdminUserSelected => SelectedUser?.Username.Equals("admin", StringComparison.OrdinalIgnoreCase) == true;
    public bool CanAssignSelectedUser => SelectedUser is not null && !IsAdminUserSelected;
    public bool CanDeleteRole => SelectedRole is { IsSystem: false, AssignedUsers: 0 };
    public string RoleModeTitle => IsNewRole ? "دور جديد" : IsEditingSystemRole ? "تعديل صلاحيات الدور الأساسي" : "صلاحيات الدور المحدد";

    public RelayCommand RefreshCommand { get; }
    public RelayCommand NewRoleCommand { get; }
    public RelayCommand SaveRoleCommand { get; }
    public RelayCommand DeleteRoleCommand { get; }
    public RelayCommand SaveAssignmentCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand ClearAllCommand { get; }

    public PermissionsViewModel(PermissionsRepository repository, UserSession actor)
    {
        this.repository = repository;
        this.actor = actor;
        RefreshCommand = new RelayCommand(Refresh);
        NewRoleCommand = new RelayCommand(BeginNewRole);
        SaveRoleCommand = new RelayCommand(SaveRole);
        DeleteRoleCommand = new RelayCommand(DeleteRole);
        SaveAssignmentCommand = new RelayCommand(SaveAssignment);
        SelectAllCommand = new RelayCommand(() => SetAllPermissions(true));
        ClearAllCommand = new RelayCommand(() => SetAllPermissions(false));
        Refresh();
    }

    private void Refresh(int? preferredRoleId = null, int? preferredUserId = null)
    {
        if (!actor.HasPermission("SECURITY.MANAGE"))
        {
            StatusMessage = "لا تملك صلاحية إدارة الأدوار والصلاحيات.";
            return;
        }
        IsBusy = true;
        try
        {
            var data = repository.Load(preferredRoleId ?? SelectedRole?.Id);
            catalog = data.Permissions;
            Roles.Clear();
            foreach (var role in data.Roles) Roles.Add(new PermissionRoleViewModel(role));
            RoleOptions.Clear();
            RoleOptions.Add(new PermissionRoleOption(-1, "بدون دور (منع افتراضي)"));
            foreach (var role in data.Roles) RoleOptions.Add(new PermissionRoleOption(role.Id, role.Name));
            Users.Clear();
            foreach (var user in data.Users) Users.Add(new PermissionUserViewModel(user));
            SelectedRole = Roles.FirstOrDefault(r => r.Id == preferredRoleId) ?? Roles.FirstOrDefault();
            SelectedUser = Users.FirstOrDefault(u => u.Id == preferredUserId) ?? Users.FirstOrDefault(u => u.Username.Equals("admin", StringComparison.OrdinalIgnoreCase)) ?? Users.FirstOrDefault();
            StatusMessage = $"تم تحميل {Roles.Count} دورًا و{data.Permissions.Count} صلاحية و{Users.Count} مستخدم.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }

    private IReadOnlyList<PermissionCatalogRecord> catalog = Array.Empty<PermissionCatalogRecord>();

    private void LoadSelectedRolePermissions() => LoadSelectedRolePermissions(null);

    private void LoadSelectedRolePermissions(IReadOnlySet<string>? selectedCodes)
    {
        if (SelectedRole is null) { PermissionGroups.Clear(); return; }
        try
        {
            selectedCodes ??= repository.Load(SelectedRole.Id).SelectedRolePermissions;
            var grouped = catalog.GroupBy(p => p.Module).OrderBy(g => g.Key, StringComparer.CurrentCulture);
            PermissionGroups.Clear();
            foreach (var group in grouped)
                PermissionGroups.Add(new PermissionGroupViewModel(group.Key,
                    group.OrderBy(p => p.Name, StringComparer.CurrentCulture)
                        .Select(p => new PermissionEntryViewModel(p, selectedCodes.Contains(p.Code)))));
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    private void LoadRoleProperties(PermissionRoleViewModel? role)
    {
        IsNewRole = false;
        RoleName = role?.Name ?? "";
        RoleDescription = role?.Description ?? "";
    }

    private void BeginNewRole()
    {
        SelectedRole = null;
        IsNewRole = true;
        RoleName = "";
        RoleDescription = "";
        PermissionGroups.Clear();
        foreach (var group in catalog.GroupBy(p => p.Module).OrderBy(g => g.Key, StringComparer.CurrentCulture))
            PermissionGroups.Add(new PermissionGroupViewModel(group.Key,
                group.OrderBy(p => p.Name, StringComparer.CurrentCulture).Select(p => new PermissionEntryViewModel(p, false))));
        StatusMessage = "حدد الصلاحيات للدور الجديد ثم احفظ.";
    }

    private void SaveRole()
    {
        IsBusy = true;
        try
        {
            var selectedCodes = PermissionGroups.SelectMany(g => g.Permissions).Where(p => p.IsGranted).Select(p => p.Code).ToArray();
            if (IsNewRole)
            {
                var created = repository.CreateRole(RoleName, RoleDescription, selectedCodes);
                StatusMessage = "تم إنشاء الدور وحفظ صلاحياته.";
                Refresh(created.Id);
            }
            else if (SelectedRole is not null)
            {
                repository.SaveRole(SelectedRole.Id, RoleName, RoleDescription, selectedCodes);
                StatusMessage = "تم حفظ الدور وصلاحياته.";
                Refresh(SelectedRole.Id);
            }
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private void DeleteRole()
    {
        if (SelectedRole is null) return;
        try
        {
            repository.DeleteRole(SelectedRole.Id);
            StatusMessage = "تم حذف الدور.";
            Refresh();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    private void SaveAssignment()
    {
        if (SelectedUser is null) return;
        try
        {
            int? roleId = SelectedUserRole is { Id: > 0 } role ? role.Id : null;
            repository.AssignRole(SelectedUser.Id, roleId);
            StatusMessage = "تم حفظ الدور للمستخدم. سيُحدّث أثر الصلاحيات عند تسجيل دخوله التالي.";
            Refresh(SelectedRole?.Id, SelectedUser.Id);
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    private void SetAllPermissions(bool granted)
    {
        foreach (var permission in PermissionGroups.SelectMany(g => g.Permissions)) permission.IsGranted = granted;
    }
}
