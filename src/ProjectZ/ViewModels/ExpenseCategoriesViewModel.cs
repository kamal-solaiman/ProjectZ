using System.Collections.ObjectModel;
using System.Globalization;
using ProjectZ.Data;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

/// <summary>One row of the «تصنيفات المصروفات» table.</summary>
public sealed class ExpenseCategoryListItemViewModel : ObservableObject
{
    private int orderNumber;
    private string name;
    private string description;
    private bool isEditing;
    private string editingName;
    private string editingDescription;

    public int Id { get; }

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

    public string Name
    {
        get => name;
        internal set
        {
            if (name == value) return;
            name = value;
            Changed();
        }
    }

    public string Description
    {
        get => description;
        internal set
        {
            if (description == value) return;
            description = value;
            Changed();
            Changed(nameof(HasDescription));
            Changed(nameof(DescriptionDisplay));
        }
    }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public string DescriptionDisplay => HasDescription ? Description : "بدون وصف";

    public bool IsEditing
    {
        get => isEditing;
        internal set
        {
            if (isEditing == value) return;
            isEditing = value;
            Changed();
            Changed(nameof(IsNotEditing));
        }
    }

    public bool IsNotEditing => !IsEditing;

    public string EditingName
    {
        get => editingName;
        set
        {
            if (editingName == value) return;
            editingName = value;
            Changed();
        }
    }

    public string EditingDescription
    {
        get => editingDescription;
        set
        {
            if (editingDescription == value) return;
            editingDescription = value;
            Changed();
        }
    }

    public ActionCommand BeginEditCommand { get; }
    public ActionCommand SaveEditCommand { get; }
    public ActionCommand CancelEditCommand { get; }
    public ActionCommand DeleteCommand { get; }

    public ExpenseCategoryListItemViewModel(
        int orderNumber,
        ExpenseCategoryRecord record,
        Action<ExpenseCategoryListItemViewModel> onBeginEdit,
        Action<ExpenseCategoryListItemViewModel> onSaveEdit,
        Action<ExpenseCategoryListItemViewModel> onCancelEdit,
        Action<ExpenseCategoryListItemViewModel> onDelete)
    {
        Id = record.Id;
        this.orderNumber = orderNumber;
        name = record.Name;
        description = record.Description;
        editingName = record.Name;
        editingDescription = record.Description;

        BeginEditCommand = new ActionCommand(() => onBeginEdit(this));
        SaveEditCommand = new ActionCommand(() => onSaveEdit(this));
        CancelEditCommand = new ActionCommand(() => onCancelEdit(this));
        DeleteCommand = new ActionCommand(() => onDelete(this));
    }

    internal void ApplySavedRecord(ExpenseCategoryRecord record)
    {
        Name = record.Name;
        Description = record.Description;
        IsEditing = false;
        EditingName = record.Name;
        EditingDescription = record.Description;
    }
}

/// <summary>
/// «تصنيفات المصروفات»: adds, renames and deletes the expense sections («إيجار»، «فواتير»…)
/// stored in dbo.ExpenseCategories. The screen writes only that table plus its AuditLog row.
/// </summary>
public sealed class ExpenseCategoriesViewModel : ObservableObject
{
    private readonly ExpenseCategoryRepository? repository;

    private List<ExpenseCategoryRecord> loadedRecords = [];
    private string newCategoryName = "";
    private string newCategoryDescription = "";
    private string searchText = "";
    private string statusMessage =
        "تُحمّل تصنيفات المصروفات من قاعدة البيانات. هذه الشاشة تدير الأقسام فقط؛ شاشتا «اضافة مصروف» و«فواتير المصروفات» لم تُنفذا بعد.";
    private bool isErrorStatus;
    private int designNextId = 1;

    public ObservableCollection<ExpenseCategoryListItemViewModel> FilteredCategories { get; } = [];

    public Action<ConfirmActionViewModel, Action>? OpenConfirmDialogRequested { get; set; }

    /// <summary>Raised after a successful write so the future expense screens can refresh their pickers.</summary>
    public Action<int>? AnalyzeCategoryRequested { get; set; }
    public RelayCommand<ExpenseCategoryListItemViewModel> AnalyzeCategoryCommand { get; }

    public Action? CategoriesChanged { get; set; }

    public string NewCategoryName
    {
        get => newCategoryName;
        set
        {
            if (newCategoryName == value) return;
            newCategoryName = value;
            Changed();
        }
    }

    public string NewCategoryDescription
    {
        get => newCategoryDescription;
        set
        {
            if (newCategoryDescription == value) return;
            newCategoryDescription = value;
            Changed();
        }
    }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (searchText == value) return;
            searchText = value;
            Changed();
            RefreshFilteredCategories();
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

    /// <summary>True when the screen reads and writes SQL; false in the design preview.</summary>
    public bool IsDatabaseBacked => repository is not null;

    public bool IsErrorStatus
    {
        get => isErrorStatus;
        private set
        {
            if (isErrorStatus == value) return;
            isErrorStatus = value;
            Changed();
        }
    }

    public int TotalCategoriesCount => FilteredCategories.Count;
    public string TotalCountDisplay => TotalCategoriesCount == 1
        ? "تصنيف واحد"
        : $"{TotalCategoriesCount} تصنيف";
    public int WithDescriptionCount => loadedRecords.Count(r => !string.IsNullOrWhiteSpace(r.Description));
    public string WithDescriptionDisplay => $"{WithDescriptionCount} مع وصف";
    public int WithoutDescriptionCount => loadedRecords.Count(r => string.IsNullOrWhiteSpace(r.Description));
    public string WithoutDescriptionDisplay => $"{WithoutDescriptionCount} بدون وصف";
    public bool HasNoCategories => FilteredCategories.Count == 0;
    public string EmptyStateText => string.IsNullOrWhiteSpace(SearchText)
        ? "لا توجد تصنيفات مصروفات مسجلة حاليًا. أضف أول قسم من الأعلى، مثل: إيجار أو فواتير."
        : "لا توجد تصنيفات مطابقة للبحث الحالي.";

    public ActionCommand AddCategoryCommand { get; }
    public ActionCommand ClearSearchCommand { get; }
    public ActionCommand RefreshCategoriesCommand { get; }

    public ExpenseCategoriesViewModel(ExpenseCategoryRepository? repository = null)
    {
        this.repository = repository;

        AnalyzeCategoryCommand = new RelayCommand<ExpenseCategoryListItemViewModel>(row =>
        {
            if (row is { Id: > 0 }) AnalyzeCategoryRequested?.Invoke(row.Id);
        }, row => row is { Id: > 0 });
        AddCategoryCommand = new ActionCommand(AddCategory);
        ClearSearchCommand = new ActionCommand(() => SearchText = "");
        RefreshCategoriesCommand = new ActionCommand(() => RefreshFromDatabase());

        if (repository is null)
        {
            // Design previews keep the same six starter sections so the screen stays explorable.
            loadedRecords = ExpenseDesignSeedData.CreateSampleExpenseCategories()
                .Select(item => new ExpenseCategoryRecord
                {
                    Id = designNextId++,
                    Name = item.Name,
                    Description = item.Description
                })
                .ToList();
        }

        RefreshFilteredCategories();
    }

    /// <summary>Reloads the expense sections from SQL Server.</summary>
    public bool RefreshFromDatabase()
    {
        if (repository is null) return false;

        try
        {
            loadedRecords = repository.LoadAll();
        }
        catch (Exception ex)
        {
            SetStatus(DescribeError(ex, "تعذر تحميل تصنيفات المصروفات من قاعدة البيانات."), true);
            return false;
        }

        RefreshFilteredCategories();
        SetStatus($"تم تحميل {TotalCategoriesCount} تصنيف مصروفات من قاعدة البيانات.", false);
        return true;
    }

    private void AddCategory()
    {
        var trimmed = NewCategoryName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            SetStatus("يرجى إدخال اسم تصنيف المصروف الجديد أولًا.", true);
            return;
        }

        if (repository is not null)
        {
            ExpenseCategoryRecord saved;
            try
            {
                saved = repository.Create(trimmed, NewCategoryDescription);
            }
            catch (Exception ex)
            {
                SetStatus(DescribeError(ex, "تعذر حفظ تصنيف المصروف في قاعدة البيانات."), true);
                return;
            }

            loadedRecords.Add(saved);
            loadedRecords = loadedRecords.OrderBy(r => r.Name, StringComparer.CurrentCulture).ToList();
            NewCategoryName = "";
            NewCategoryDescription = "";
            RefreshFilteredCategories();
            SetStatus($"تم حفظ تصنيف المصروف «{saved.Name}» في قاعدة البيانات.", false);
            CategoriesChanged?.Invoke();
            return;
        }

        if (loadedRecords.Any(r => r.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus($"تصنيف المصروف «{trimmed}» موجود بالفعل في القائمة.", true);
            return;
        }

        loadedRecords.Add(new ExpenseCategoryRecord
        {
            Id = designNextId++,
            Name = trimmed,
            Description = NewCategoryDescription.Trim()
        });
        NewCategoryName = "";
        NewCategoryDescription = "";
        RefreshFilteredCategories();
        SetStatus($"تمت إضافة تصنيف المصروف «{trimmed}» في وضع العرض التجريبي.", false);
    }

    private void BeginEdit(ExpenseCategoryListItemViewModel item)
    {
        foreach (var entry in FilteredCategories)
        {
            if (!ReferenceEquals(entry, item) && entry.IsEditing)
            {
                entry.IsEditing = false;
                entry.EditingName = entry.Name;
                entry.EditingDescription = entry.Description;
            }
        }

        item.EditingName = item.Name;
        item.EditingDescription = item.Description;
        item.IsEditing = true;
    }

    private void SaveEdit(ExpenseCategoryListItemViewModel item)
    {
        var trimmed = item.EditingName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            SetStatus("اسم تصنيف المصروف لا يمكن أن يكون فارغًا.", true);
            return;
        }

        if (trimmed == item.Name && item.EditingDescription.Trim() == item.Description)
        {
            item.IsEditing = false;
            SetStatus($"لم يتم إجراء تغيير على تصنيف المصروف «{item.Name}».", false);
            return;
        }

        if (repository is not null)
        {
            ExpenseCategoryRecord saved;
            try
            {
                saved = repository.Rename(item.Id, trimmed, item.EditingDescription);
            }
            catch (Exception ex)
            {
                SetStatus(DescribeError(ex, "تعذر تعديل تصنيف المصروف في قاعدة البيانات."), true);
                return;
            }

            var previousName = item.Name;
            var index = loadedRecords.FindIndex(r => r.Id == item.Id);
            if (index >= 0) loadedRecords[index] = saved;

            loadedRecords = loadedRecords.OrderBy(r => r.Name, StringComparer.CurrentCulture).ToList();
            item.ApplySavedRecord(saved);
            RefreshFilteredCategories();
            SetStatus(previousName == saved.Name
                ? $"تم حفظ وصف تصنيف المصروف «{saved.Name}» في قاعدة البيانات."
                : $"تم تعديل تصنيف المصروف من «{previousName}» إلى «{saved.Name}» في قاعدة البيانات.",
                false);
            CategoriesChanged?.Invoke();
            return;
        }

        if (loadedRecords.Any(r => r.Id != item.Id && r.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus($"تصنيف المصروف «{trimmed}» موجود بالفعل في القائمة.", true);
            return;
        }

        var recordIndex = loadedRecords.FindIndex(r => r.Id == item.Id);
        if (recordIndex >= 0)
        {
            loadedRecords[recordIndex] = new ExpenseCategoryRecord
            {
                Id = item.Id,
                Name = trimmed,
                Description = item.EditingDescription.Trim()
            };
        }

        item.IsEditing = false;
        item.Name = trimmed;
        item.Description = item.EditingDescription.Trim();
        RefreshFilteredCategories();
        SetStatus($"تم تعديل تصنيف المصروف إلى «{trimmed}» في وضع العرض التجريبي.", false);
    }

    private void CancelEdit(ExpenseCategoryListItemViewModel item)
    {
        item.IsEditing = false;
        item.EditingName = item.Name;
        item.EditingDescription = item.Description;
    }

    private void RequestDelete(ExpenseCategoryListItemViewModel item)
    {
        var categoryName = item.Name;
        var confirmVm = new ConfirmActionViewModel(
            "تأكيد حذف تصنيف مصروف",
            categoryName,
            $"هل تريد حذف تصنيف المصروف «{categoryName}» من قاعدة البيانات؟ لن يظهر بعدها في قوائم اختيار المصروفات. إذا كانت هناك مصروفات مسجلة عليه سيُرفض الحذف حتى تغيّر تصنيفها.",
            "حذف التصنيف");

        if (OpenConfirmDialogRequested is not null)
        {
            OpenConfirmDialogRequested(confirmVm, () => DeleteConfirmed(item));
        }
        else
        {
            DeleteConfirmed(item);
        }
    }

    private void DeleteConfirmed(ExpenseCategoryListItemViewModel item)
    {
        if (repository is not null)
        {
            try
            {
                repository.Delete(item.Id);
            }
            catch (Exception ex)
            {
                SetStatus(DescribeError(ex, "تعذر حذف تصنيف المصروف من قاعدة البيانات."), true);
                return;
            }

            loadedRecords.RemoveAll(r => r.Id == item.Id);
            RefreshFilteredCategories();
            SetStatus($"تم حذف تصنيف المصروف «{item.Name}» من قاعدة البيانات.", false);
            CategoriesChanged?.Invoke();
            return;
        }

        loadedRecords.RemoveAll(r => r.Id == item.Id);
        RefreshFilteredCategories();
        SetStatus($"تم حذف تصنيف المصروف «{item.Name}» من القائمة.", false);
    }

    private void RefreshFilteredCategories()
    {
        var query = searchText.Trim();
        var rows = loadedRecords.AsEnumerable();

        if (!string.IsNullOrEmpty(query))
        {
            rows = rows.Where(r =>
                r.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        FilteredCategories.Clear();
        var order = 1;
        foreach (var record in rows.OrderBy(r => r.Name, StringComparer.CurrentCulture))
        {
            FilteredCategories.Add(new ExpenseCategoryListItemViewModel(
                order++,
                record,
                BeginEdit,
                SaveEdit,
                CancelEdit,
                RequestDelete));
        }

        Changed(nameof(TotalCategoriesCount));
        Changed(nameof(TotalCountDisplay));
        Changed(nameof(WithDescriptionCount));
        Changed(nameof(WithDescriptionDisplay));
        Changed(nameof(WithoutDescriptionCount));
        Changed(nameof(WithoutDescriptionDisplay));
        Changed(nameof(HasNoCategories));
        Changed(nameof(EmptyStateText));
    }

    private static string DescribeError(Exception exception, string fallback)
        => exception is ExpenseCategoryDataException data ? data.Message : fallback;

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
