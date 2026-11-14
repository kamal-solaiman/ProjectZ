using System.Collections.ObjectModel;
using System.Globalization;
using ProjectZ.Data;
using ProjectZ.Helpers;

namespace ProjectZ.ViewModels;

/// <summary>One row of the users table.</summary>
public sealed class UserListItemViewModel : ObservableObject
{
    private int orderNumber;
    private string username;
    private string displayName;
    private bool isActive;
    private int linkedRecordsCount;
    private DateTime updatedAtUtc;

    public int Id { get; }
    public bool IsCurrentUser { get; }

    public int OrderNumber
    {
        get => orderNumber;
        internal set
        {
            if (orderNumber == value) return;
            orderNumber = value;
            Changed();
            Changed(nameof(OrderDisplay));
        }
    }

    public string OrderDisplay => OrderNumber.ToString(CultureInfo.InvariantCulture);

    public string Username
    {
        get => username;
        internal set
        {
            if (username == value) return;
            username = value;
            Changed();
        }
    }

    public string DisplayName
    {
        get => displayName;
        internal set
        {
            if (displayName == value) return;
            displayName = value;
            Changed();
            Changed(nameof(DisplayNameOrUsername));
        }
    }

    public string DisplayNameOrUsername => string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName;

    public bool IsActive
    {
        get => isActive;
        internal set
        {
            if (isActive == value) return;
            isActive = value;
            Changed();
            Changed(nameof(StatusDisplay));
            Changed(nameof(CanDelete));
        }
    }

    public string StatusDisplay => IsActive ? "نشط" : "معطّل";

    public int LinkedRecordsCount
    {
        get => linkedRecordsCount;
        private set
        {
            if (linkedRecordsCount == value) return;
            linkedRecordsCount = value;
            Changed();
            Changed(nameof(HasLinkedRecords));
            Changed(nameof(LinkedRecordsDisplay));
            Changed(nameof(CanDelete));
        }
    }

    public bool HasLinkedRecords => LinkedRecordsCount > 0;

    public string LinkedRecordsDisplay => LinkedRecordsCount == 0
        ? "لا يوجد ربط"
        : LinkedRecordsCount == 1 ? "سجل واحد" : $"{LinkedRecordsCount} سجل";

    /// <summary>The database refuses the delete too; disabling the button only explains it earlier.</summary>
    public bool CanDelete => LinkedRecordsCount == 0 && !IsCurrentUser;

    public string DeleteToolTip => IsCurrentUser
        ? "لا يمكن حذف الحساب المستخدم في الجلسة الحالية"
        : HasLinkedRecords
            ? "مرتبط بسجلات نظام؛ استخدم «تعطيل» بدلًا من الحذف"
            : "حذف الحساب نهائيًا من قاعدة البيانات";

    public DateTime UpdatedAtUtc
    {
        get => updatedAtUtc;
        private set
        {
            if (updatedAtUtc == value) return;
            updatedAtUtc = value;
            Changed();
            Changed(nameof(UpdatedAtDisplay));
        }
    }

    public string UpdatedAtDisplay => UpdatedAtUtc == default
        ? "—"
        : UpdatedAtUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);

    public ActionCommand EditCommand { get; }
    public ActionCommand ChangePasswordCommand { get; }
    public ActionCommand ToggleActiveCommand { get; }
    public ActionCommand DeleteCommand { get; }

    public UserListItemViewModel(
        int orderNumber,
        UserRecord record,
        int currentUserId,
        Action<UserListItemViewModel> onEdit,
        Action<UserListItemViewModel> onChangePassword,
        Action<UserListItemViewModel> onToggleActive,
        Action<UserListItemViewModel> onDelete)
    {
        Id = record.Id;
        IsCurrentUser = record.Id == currentUserId;
        this.orderNumber = orderNumber;
        username = record.Username;
        displayName = record.DisplayName;
        isActive = record.IsActive;
        linkedRecordsCount = record.LinkedRecordsCount;
        updatedAtUtc = record.UpdatedAtUtc;

        EditCommand = new ActionCommand(() => onEdit(this));
        ChangePasswordCommand = new ActionCommand(() => onChangePassword(this));
        ToggleActiveCommand = new ActionCommand(() => onToggleActive(this));
        DeleteCommand = new ActionCommand(() => onDelete(this));
    }

    internal void ApplySavedRecord(UserRecord record)
    {
        Username = record.Username;
        DisplayName = record.DisplayName;
        IsActive = record.IsActive;
        LinkedRecordsCount = record.LinkedRecordsCount;
        UpdatedAtUtc = record.UpdatedAtUtc;
    }
}

/// <summary>
/// The users screen: login accounts stored in dbo.Users.
/// Users are the accounts that sign in to ProjectZ; they are not customers or suppliers and carry no permissions.
/// </summary>
public sealed class UsersViewModel : ObservableObject
{
    private readonly UserRepository? repository;
    private readonly int currentUserId;
    private readonly string currentUsername;

    private List<UserRecord> loadedRecords = [];
    private string searchText = "";
    private string statusFilter = AllFilter;
    private string statusMessage = "";
    private bool isErrorStatus;

    private const string AllFilter = "الكل";
    private const string ActiveFilter = "نشط";
    private const string InactiveFilter = "معطّل";

    public ObservableCollection<UserListItemViewModel> FilteredUsers { get; } = [];
    public IReadOnlyList<string> StatusFilters { get; } = [AllFilter, ActiveFilter, InactiveFilter];

    // Dialog hooks wired by UsersView.xaml.cs. The view owns the dialogs and their PasswordBox readers,
    // and reports the saved row back through the callback.
    public Action<string, UserRecord?, Action<UserRecord>>? OpenUserEditorRequested { get; set; }
    public Action<ConfirmActionViewModel, Action>? OpenConfirmDialogRequested { get; set; }

    /// <summary>Raised after a successful SQL write so other screens can react if needed.</summary>
    public Action? UsersChanged { get; set; }

    public string CashierName { get; }
    public bool IsDatabaseBacked => repository is not null;

    /// <summary>The dialog needs the same repository instance to write dbo.Users.</summary>
    internal UserRepository? Repository => repository;
    public string ConnectionBadgeText => IsDatabaseBacked ? "متصل بقاعدة البيانات" : "عرض تصميم بدون قاعدة بيانات";

    public string SearchText
    {
        get => searchText;
        set
        {
            if (searchText == value) return;
            searchText = value;
            Changed();
            RefreshFilteredUsers();
        }
    }

    public string StatusFilter
    {
        get => statusFilter;
        set
        {
            if (statusFilter == value) return;
            statusFilter = value;
            Changed();
            RefreshFilteredUsers();
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (statusMessage == value) return;
            statusMessage = value;
            Changed();
            Changed(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool IsErrorStatus => isErrorStatus;

    public int TotalUsersCount => loadedRecords.Count;
    public string TotalCountDisplay => TotalUsersCount == 1 ? "مستخدم واحد" : $"{TotalUsersCount} مستخدم";
    public int ActiveUsersCount => loadedRecords.Count(r => r.IsActive);
    public string ActiveCountDisplay => $"{ActiveUsersCount} نشط";
    public int InactiveUsersCount => loadedRecords.Count(r => !r.IsActive);
    public string InactiveCountDisplay => $"{InactiveUsersCount} معطّل";
    public bool HasNoUsers => FilteredUsers.Count == 0;
    public string EmptyStateText => string.IsNullOrWhiteSpace(SearchText) && StatusFilter == AllFilter
        ? "لا يوجد مستخدمون مسجلون حاليًا. أضف مستخدمًا جديدًا من زر «إضافة مستخدم» ليتمكن من تسجيل الدخول."
        : "لا يوجد مستخدمون مطابقون للبحث أو الفلتر الحالي.";

    public ActionCommand AddUserCommand { get; }
    public ActionCommand RefreshUsersCommand { get; }
    public ActionCommand ClearSearchCommand { get; }
    public ActionCommand ShowAllCommand { get; }
    public ActionCommand ShowActiveCommand { get; }
    public ActionCommand ShowInactiveCommand { get; }

    public UsersViewModel(
        string cashierName = "المشرف",
        UserRepository? repository = null,
        int currentUserId = 0,
        string currentUsername = "")
    {
        CashierName = cashierName;
        this.repository = repository;
        this.currentUserId = currentUserId;
        this.currentUsername = currentUsername;

        AddUserCommand = new ActionCommand(BeginAddUser);
        RefreshUsersCommand = new ActionCommand(() => RefreshFromDatabase());
        ClearSearchCommand = new ActionCommand(() => SearchText = "");
        ShowAllCommand = new ActionCommand(() => StatusFilter = AllFilter);
        ShowActiveCommand = new ActionCommand(() => StatusFilter = ActiveFilter);
        ShowInactiveCommand = new ActionCommand(() => StatusFilter = InactiveFilter);

        if (repository is null)
        {
            loadedRecords =
            [
                new UserRecord { Id = 1, Username = "admin", DisplayName = "المشرف العام", IsActive = true },
                new UserRecord { Id = 2, Username = "cashier", DisplayName = "كاشير الفرع", IsActive = true },
                new UserRecord { Id = 3, Username = "old.user", DisplayName = "مستخدم قديم", IsActive = false }
            ];
            SetStatus("وضع العرض التجريبي: لا يوجد اتصال بقاعدة البيانات، ولن تُحفظ أي إضافة أو تعديل.", false);
        }

        RefreshFilteredUsers();
    }

    /// <summary>Reloads the login accounts from dbo.Users.</summary>
    public bool RefreshFromDatabase()
    {
        if (repository is null) return false;

        try
        {
            loadedRecords = repository.LoadAll();
        }
        catch (Exception ex)
        {
            SetStatus(DescribeError(ex, "تعذر تحميل المستخدمين من قاعدة البيانات."), true);
            return false;
        }

        RefreshFilteredUsers();
        SetStatus($"تم تحميل {loadedRecords.Count} حساب من جدول dbo.Users.", false);
        return true;
    }

    private void BeginAddUser()
    {
        if (repository is null)
        {
            SetStatus("وضع العرض التجريبي: لا يمكن إضافة مستخدم بدون قاعدة بيانات.", true);
            return;
        }

        OpenEditorRequested("add", null);
    }

    /// <summary>Opens the add/edit/password dialog for a row and merges the saved result into the list.</summary>
    private void OpenEditorRequested(string mode, UserRecord? record)
    {
        if (OpenUserEditorRequested is null)
        {
            SetStatus("تعذر فتح نافذة المستخدم. أعد فتح الشاشة وحاول مرة أخرى.", true);
            return;
        }

        OpenUserEditorRequested(mode, record, saved =>
        {
            ApplySavedRecord(saved);
            SetStatus(mode switch
            {
                "add" => $"تم حفظ المستخدم «{saved.Username}» في قاعدة البيانات ويمكنه تسجيل الدخول الآن.",
                "password" => $"تم تعيين كلمة مرور جديدة للحساب «{saved.Username}».",
                _ => $"تم حفظ تعديلات المستخدم «{saved.Username}» في قاعدة البيانات."
            }, false);
        });
    }

    internal void RequestDelete(UserListItemViewModel item)
    {
        if (item.IsCurrentUser)
        {
            SetStatus("لا يمكنك حذف الحساب الذي تستخدمه الآن. سجّل الدخول بحساب آخر لحذفه.", true);
            return;
        }

        if (!item.CanDelete)
        {
            SetStatus(
                $"لا يمكن حذف المستخدم «{item.Username}» لأنه مرتبط بـ {item.LinkedRecordsCount} سجل في النظام. " +
                "استخدم زر «تعطيل» لإيقاف دخوله مع الحفاظ على السجلات.",
                true);
            return;
        }

        var username = item.Username;
        var confirmVm = new ConfirmActionViewModel(
            "تأكيد حذف المستخدم",
            username,
            $"هل تريد حذف حساب «{username}» نهائيًا من جدول dbo.Users؟ لن يستطيع صاحبه تسجيل الدخول بعد ذلك.",
            "حذف المستخدم");

        if (OpenConfirmDialogRequested is not null)
            OpenConfirmDialogRequested(confirmVm, () => DeleteConfirmed(item));
        else
            DeleteConfirmed(item);
    }

    private void DeleteConfirmed(UserListItemViewModel item)
    {
        if (repository is null)
        {
            SetStatus("وضع العرض التجريبي: لم يُحذف المستخدم.", true);
            return;
        }

        try
        {
            repository.Delete(item.Id);
        }
        catch (Exception ex)
        {
            SetStatus(DescribeError(ex, "تعذر حذف المستخدم من قاعدة البيانات."), true);
            return;
        }

        loadedRecords.RemoveAll(r => r.Id == item.Id);
        RefreshFilteredUsers();
        SetStatus($"تم حذف حساب «{item.Username}» من قاعدة البيانات.", false);
        UsersChanged?.Invoke();
    }

    private void ToggleActive(UserListItemViewModel item)
    {
        if (item.IsCurrentUser && item.IsActive)
        {
            SetStatus("لا يمكنك تعطيل الحساب الذي تستخدمه الآن. اطلب من مستخدم آخر تعطيله.", true);
            return;
        }

        var next = !item.IsActive;
        if (repository is null)
        {
            item.ApplySavedRecord(new UserRecord
            {
                Id = item.Id,
                Username = item.Username,
                DisplayName = item.DisplayName,
                IsActive = next,
                UpdatedAtUtc = DateTime.UtcNow,
                LinkedRecordsCount = item.LinkedRecordsCount
            });
            RefreshFilteredUsers();
            SetStatus("وضع العرض التجريبي: لم يُحفظ تغيير حالة الحساب.", true);
            return;
        }

        try
        {
            var saved = repository.Update(item.Id, item.Username, item.DisplayName, next);
            item.ApplySavedRecord(saved);
        }
        catch (Exception ex)
        {
            SetStatus(DescribeError(ex, "تعذر تغيير حالة المستخدم في قاعدة البيانات."), true);
            return;
        }

        RefreshFilteredUsers();
        SetStatus(next
            ? $"تم تنشيط الحساب «{item.Username}» ويمكنه تسجيل الدخول الآن."
            : $"تم تعطيل الحساب «{item.Username}» ولن يستطيع تسجيل الدخول حتى يُعاد تنشيطه.",
            false);
        UsersChanged?.Invoke();
    }

    internal void ApplySavedRecord(UserRecord record)
    {
        var index = loadedRecords.FindIndex(r => r.Id == record.Id);
        if (index >= 0) loadedRecords[index] = record;
        else loadedRecords.Add(record);

        loadedRecords = loadedRecords.OrderBy(r => r.Username, StringComparer.CurrentCulture).ToList();
        RefreshFilteredUsers();
        UsersChanged?.Invoke();
    }

    private void RefreshFilteredUsers()
    {
        var query = searchText.Trim();
        var rows = loadedRecords.AsEnumerable();

        if (!string.IsNullOrEmpty(query))
        {
            rows = rows.Where(r =>
                r.Username.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        rows = statusFilter switch
        {
            ActiveFilter => rows.Where(r => r.IsActive),
            InactiveFilter => rows.Where(r => !r.IsActive),
            _ => rows
        };

        FilteredUsers.Clear();
        var order = 1;
        foreach (var record in rows.OrderBy(r => r.Username, StringComparer.CurrentCulture))
        {
            FilteredUsers.Add(new UserListItemViewModel(
                order++,
                record,
                currentUserId,
                OpenEdit,
                OpenChangePassword,
                ToggleActive,
                RequestDelete));
        }

        Changed(nameof(TotalUsersCount));
        Changed(nameof(TotalCountDisplay));
        Changed(nameof(ActiveUsersCount));
        Changed(nameof(ActiveCountDisplay));
        Changed(nameof(InactiveUsersCount));
        Changed(nameof(InactiveCountDisplay));
        Changed(nameof(HasNoUsers));
        Changed(nameof(EmptyStateText));
    }

    private void OpenEdit(UserListItemViewModel item)
    {
        if (repository is null)
        {
            SetStatus("وضع العرض التجريبي: لا يمكن تعديل مستخدم بدون قاعدة بيانات.", true);
            return;
        }

        if (loadedRecords.FirstOrDefault(r => r.Id == item.Id) is { } record)
            OpenEditorRequested("edit", record);
    }

    private void OpenChangePassword(UserListItemViewModel item)
    {
        if (repository is null)
        {
            SetStatus("وضع العرض التجريبي: لا يمكن تغيير كلمة المرور بدون قاعدة بيانات.", true);
            return;
        }

        if (loadedRecords.FirstOrDefault(r => r.Id == item.Id) is { } record)
            OpenEditorRequested("password", record);
    }

    internal void SetStatus(string message, bool isError)
    {
        isErrorStatus = isError;
        StatusMessage = message;
        Changed(nameof(IsErrorStatus));
    }

    public string CurrentUserHint => string.IsNullOrWhiteSpace(currentUsername)
        ? ""
        : $"أنت مسجّل الدخول باسم «{currentUsername}».";

    private static string DescribeError(Exception exception, string fallback)
        => exception is UserDataException data ? data.Message : fallback;
}
