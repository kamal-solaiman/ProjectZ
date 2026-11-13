using System.Collections.ObjectModel;
using System.Globalization;
using ProjectZ.Data;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class CategoryListItemViewModel : ObservableObject
{
    private int orderNumber;
    private string name;
    private string description;
    private int productsCount;
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

    /// <summary>Total products in this category (archived included), used to explain a refused delete.</summary>
    public int ProductsCount
    {
        get => productsCount;
        private set
        {
            if (productsCount == value) return;
            productsCount = value;
            Changed();
            Changed(nameof(ProductsCountDisplay));
            Changed(nameof(CanDelete));
        }
    }

    public int ActiveProductsCount { get; private init; }

    public string ProductsCountDisplay => ProductsCount == 1
        ? "صنف واحد"
        : $"{ProductsCount} صنف";

    public bool HasProducts => ProductsCount > 0;

    /// <summary>Only an unused category may be deleted; the database refuses the rest as a second guard.</summary>
    public bool CanDelete => ProductsCount == 0;

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

    public CategoryListItemViewModel(
        int orderNumber,
        CategoryRecord record,
        Action<CategoryListItemViewModel> onBeginEdit,
        Action<CategoryListItemViewModel> onSaveEdit,
        Action<CategoryListItemViewModel> onCancelEdit,
        Action<CategoryListItemViewModel> onDelete)
    {
        Id = record.Id;
        this.orderNumber = orderNumber;
        name = record.Name;
        description = record.Description;
        productsCount = record.ProductsCount;
        ActiveProductsCount = record.ActiveProductsCount;
        editingName = record.Name;
        editingDescription = record.Description;

        BeginEditCommand = new ActionCommand(() => onBeginEdit(this));
        SaveEditCommand = new ActionCommand(() => onSaveEdit(this));
        CancelEditCommand = new ActionCommand(() => onCancelEdit(this));
        DeleteCommand = new ActionCommand(() => onDelete(this));
    }

    internal void ApplySavedRecord(CategoryRecord record)
    {
        Name = record.Name;
        Description = record.Description;
        ProductsCount = record.ProductsCount;
        IsEditing = false;
        EditingName = record.Name;
        EditingDescription = record.Description;
    }
}

public sealed class CategoriesViewModel : ObservableObject
{
    private readonly ObservableCollection<string> sharedCategories;
    private readonly CategoryRepository? repository;
    private readonly Action? navigateToAddProduct;

    private List<CategoryRecord> loadedRecords = [];
    private string newCategoryName = "";
    private string newCategoryDescription = "";
    private string searchText = "";
    private bool showOnlyUsed;
    private string statusMessage = "تُحمّل التصنيفات من قاعدة البيانات، وأي إضافة أو تعديل يظهر فورًا في قائمة التصنيفات بشاشة إضافة منتج.";
    private bool isErrorStatus;

    public ObservableCollection<CategoryListItemViewModel> FilteredCategories { get; } = [];

    public Action<ConfirmActionViewModel, Action>? OpenConfirmDialogRequested { get; set; }

    /// <summary>Raised after a successful SQL write so other screens can refresh their own lists.</summary>
    public Action? CategoriesChanged { get; set; }

    public string CashierName { get; }

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

    /// <summary>Filter that keeps only categories already used by at least one product.</summary>
    public bool ShowOnlyUsed
    {
        get => showOnlyUsed;
        set
        {
            if (showOnlyUsed == value) return;
            showOnlyUsed = value;
            Changed();
            RefreshFilteredCategories();
        }
    }

    public ActionCommand ToggleOnlyUsedCommand { get; }

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
    public string TotalCountDisplay => $"{TotalCategoriesCount} تصنيف";
    public int EmptyCategoriesCount => loadedRecords.Count(r => r.ProductsCount == 0);
    public string EmptyCategoriesDisplay => $"{EmptyCategoriesCount} بدون أصناف";
    public int UsedCategoriesCount => loadedRecords.Count(r => r.ProductsCount > 0);
    public string UsedCategoriesDisplay => $"{UsedCategoriesCount} مستخدم";
    public bool HasNoCategories => FilteredCategories.Count == 0;
    public string EmptyStateText => string.IsNullOrWhiteSpace(SearchText)
        ? "لا توجد تصنيفات مسجلة حاليًا. أضف تصنيفًا جديدًا من الأعلى ليظهر في قائمة إضافة منتج."
        : "لا توجد تصنيفات مطابقة للبحث الحالي.";

    public ActionCommand AddCategoryCommand { get; }
    public ActionCommand ClearSearchCommand { get; }
    public ActionCommand GoToAddProductCommand { get; }
    public ActionCommand RefreshCategoriesCommand { get; }

    public CategoriesViewModel() : this(PosDesignSeedData.CreateSharedCategories(), null)
    {
    }

    public CategoriesViewModel(
        ObservableCollection<string> sharedCategories,
        Action? navigateToAddProduct = null,
        CategoryRepository? repository = null,
        string cashierName = "المشرف")
    {
        this.sharedCategories = sharedCategories;
        this.navigateToAddProduct = navigateToAddProduct;
        this.repository = repository;
        CashierName = cashierName;

        AddCategoryCommand = new ActionCommand(AddCategory);
        ClearSearchCommand = new ActionCommand(() => SearchText = "");
        GoToAddProductCommand = new ActionCommand(() => this.navigateToAddProduct?.Invoke());
        RefreshCategoriesCommand = new ActionCommand(() => RefreshFromDatabase());
        ToggleOnlyUsedCommand = new ActionCommand(() => ShowOnlyUsed = !ShowOnlyUsed);

        if (repository is null)
        {
            // Design previews keep the seeded names so the screen is still explorable without a database.
            loadedRecords = sharedCategories
                .Select(name => new CategoryRecord { Name = name })
                .ToList();
        }

        RefreshFilteredCategories();
    }

    /// <summary>
    /// Saves one category from any screen that needs it (the add-product dialog). Returns the stored name,
    /// or the existing name when the category is already there, so the caller can select it.
    /// </summary>
    public string EnsureCategory(string name, string description = "")
    {
        var trimmed = name.Trim();
        if (repository is null)
        {
            return sharedCategories.FirstOrDefault(c => c.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
                   ?? trimmed;
        }

        var existing = loadedRecords.FirstOrDefault(r => r.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
                       ?? repository.LoadAll().FirstOrDefault(r => r.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing.Name;

        CategoryRecord saved;
        try
        {
            saved = repository.Create(trimmed, description);
        }
        catch (CategoryDataException)
        {
            // Another user created the same name between the check and the insert: use theirs.
            var concurrent = repository.LoadAll()
                .FirstOrDefault(r => r.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
            if (concurrent is null) throw;
            if (!sharedCategories.Contains(concurrent.Name)) sharedCategories.Add(concurrent.Name);
            return concurrent.Name;
        }

        loadedRecords.Add(saved);
        loadedRecords = loadedRecords.OrderBy(r => r.Name, StringComparer.CurrentCulture).ToList();
        if (!sharedCategories.Contains(saved.Name)) sharedCategories.Add(saved.Name);
        RefreshFilteredCategories();
        CategoriesChanged?.Invoke();
        return saved.Name;
    }

    /// <summary>Reloads the category rows (and the add-product combo box source) from SQL Server.</summary>
    public bool RefreshFromDatabase()
    {
        if (repository is null) return false;

        try
        {
            loadedRecords = repository.LoadAll();
            repository.LoadNamesInto(sharedCategories);
        }
        catch (Exception ex)
        {
            SetStatus(DescribeError(ex, "تعذر تحميل التصنيفات من قاعدة البيانات."), true);
            return false;
        }

        RefreshFilteredCategories();
        SetStatus($"تم تحميل {loadedRecords.Count} تصنيف من قاعدة البيانات.", false);
        return true;
    }

    private void AddCategory()
    {
        var trimmed = NewCategoryName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            SetStatus("يرجى إدخال اسم التصنيف الجديد أولًا.", true);
            return;
        }

        if (repository is not null)
        {
            CategoryRecord saved;
            try
            {
                saved = repository.Create(trimmed, NewCategoryDescription);
            }
            catch (Exception ex)
            {
                SetStatus(DescribeError(ex, "تعذر حفظ التصنيف في قاعدة البيانات."), true);
                return;
            }

            loadedRecords.Add(saved);
            loadedRecords = loadedRecords.OrderBy(r => r.Name, StringComparer.CurrentCulture).ToList();
            if (!sharedCategories.Contains(saved.Name)) sharedCategories.Add(saved.Name);

            NewCategoryName = "";
            NewCategoryDescription = "";
            RefreshFilteredCategories();
            SetStatus($"تم حفظ التصنيف «{saved.Name}» في قاعدة البيانات وأصبح متاحًا في قائمة إضافة منتج.", false);
            CategoriesChanged?.Invoke();
            return;
        }

        if (sharedCategories.Any(c => c.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus($"التصنيف «{trimmed}» موجود بالفعل في القائمة.", true);
            return;
        }

        sharedCategories.Add(trimmed);
        loadedRecords.Add(new CategoryRecord { Name = trimmed, Description = NewCategoryDescription.Trim() });
        NewCategoryName = "";
        NewCategoryDescription = "";
        RefreshFilteredCategories();
        SetStatus($"تمت إضافة التصنيف «{trimmed}» في وضع العرض التجريبي.", false);
    }

    private void BeginEdit(CategoryListItemViewModel item)
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

    private void SaveEdit(CategoryListItemViewModel item)
    {
        var trimmed = item.EditingName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            SetStatus("اسم التصنيف لا يمكن أن يكون فارغًا.", true);
            return;
        }

        if (trimmed == item.Name && item.EditingDescription.Trim() == item.Description)
        {
            item.IsEditing = false;
            SetStatus($"لم يتم إجراء تغيير على التصنيف «{item.Name}»", false);
            return;
        }

        if (repository is not null)
        {
            CategoryRecord saved;
            try
            {
                saved = repository.Rename(item.Id, trimmed, item.EditingDescription);
            }
            catch (Exception ex)
            {
                SetStatus(DescribeError(ex, "تعذر تعديل التصنيف في قاعدة البيانات."), true);
                return;
            }

            var previousName = item.Name;
            var index = loadedRecords.FindIndex(r => r.Id == item.Id);
            if (index >= 0) loadedRecords[index] = saved;

            // The combo box keeps plain names, so replace the old name in place and keep the selection.
            var nameIndex = sharedCategories.IndexOf(previousName);
            if (nameIndex >= 0)
                sharedCategories[nameIndex] = saved.Name;
            else if (!sharedCategories.Contains(saved.Name))
                sharedCategories.Add(saved.Name);

            loadedRecords = loadedRecords.OrderBy(r => r.Name, StringComparer.CurrentCulture).ToList();
            item.ApplySavedRecord(saved);
            RefreshFilteredCategories();
            SetStatus(previousName == saved.Name
                ? $"تم حفظ وصف التصنيف «{saved.Name}» في قاعدة البيانات."
                : $"تم تعديل التصنيف من «{previousName}» إلى «{saved.Name}» في قاعدة البيانات، وتحدّثت كل أصنافه تلقائيًا.",
                false);
            CategoriesChanged?.Invoke();
            return;
        }

        if (sharedCategories.Any(c => !c.Equals(item.Name, StringComparison.OrdinalIgnoreCase)
                                      && c.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus($"التصنيف «{trimmed}» موجود بالفعل في القائمة.", true);
            return;
        }

        var designIndex = sharedCategories.IndexOf(item.Name);
        if (designIndex >= 0) sharedCategories[designIndex] = trimmed;
        var recordIndex = loadedRecords.FindIndex(r => r.Name == item.Name);
        if (recordIndex >= 0)
        {
            loadedRecords[recordIndex] = new CategoryRecord
            {
                Id = item.Id,
                Name = trimmed,
                Description = item.EditingDescription.Trim(),
                ProductsCount = item.ProductsCount,
                ActiveProductsCount = item.ActiveProductsCount
            };
        }

        item.IsEditing = false;
        item.Name = trimmed;
        item.Description = item.EditingDescription.Trim();
        RefreshFilteredCategories();
        SetStatus($"تم تعديل التصنيف إلى «{trimmed}» في وضع العرض التجريبي.", false);
    }

    private void CancelEdit(CategoryListItemViewModel item)
    {
        item.IsEditing = false;
        item.EditingName = item.Name;
        item.EditingDescription = item.Description;
    }

    private void RequestDelete(CategoryListItemViewModel item)
    {
        if (!item.CanDelete)
        {
            SetStatus(
                $"لا يمكن حذف التصنيف «{item.Name}» لأنه مستخدم في {item.ProductsCount} صنف. " +
                "انقل هذه الأصناف إلى تصنيف آخر من شاشة تعديل الصنف ثم أعد المحاولة.",
                true);
            return;
        }

        var categoryName = item.Name;
        var confirmVm = new ConfirmActionViewModel(
            "تأكيد حذف التصنيف",
            categoryName,
            $"هل تريد حذف التصنيف «{categoryName}» من قاعدة البيانات؟ لن يظهر بعدها في قائمة التصنيفات بشاشة إضافة منتج.",
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

    private void DeleteConfirmed(CategoryListItemViewModel item)
    {
        if (repository is not null)
        {
            try
            {
                repository.Delete(item.Id);
            }
            catch (Exception ex)
            {
                SetStatus(DescribeError(ex, "تعذر حذف التصنيف من قاعدة البيانات."), true);
                return;
            }

            loadedRecords.RemoveAll(r => r.Id == item.Id);
            sharedCategories.Remove(item.Name);
            RefreshFilteredCategories();
            SetStatus($"تم حذف التصنيف «{item.Name}» من قاعدة البيانات وأُزيل من قائمة إضافة منتج.", false);
            CategoriesChanged?.Invoke();
            return;
        }

        loadedRecords.RemoveAll(r => r.Name == item.Name);
        sharedCategories.Remove(item.Name);
        RefreshFilteredCategories();
        SetStatus($"تم حذف التصنيف «{item.Name}» من القائمة.", false);
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

        if (showOnlyUsed)
            rows = rows.Where(r => r.ProductsCount > 0);

        FilteredCategories.Clear();
        var order = 1;
        foreach (var record in rows.OrderBy(r => r.Name, StringComparer.CurrentCulture))
        {
            FilteredCategories.Add(new CategoryListItemViewModel(
                order++,
                record,
                BeginEdit,
                SaveEdit,
                CancelEdit,
                RequestDelete));
        }

        Changed(nameof(TotalCategoriesCount));
        Changed(nameof(TotalCountDisplay));
        Changed(nameof(EmptyCategoriesCount));
        Changed(nameof(EmptyCategoriesDisplay));
        Changed(nameof(UsedCategoriesCount));
        Changed(nameof(UsedCategoriesDisplay));
        Changed(nameof(HasNoCategories));
        Changed(nameof(EmptyStateText));
    }

    private static string DescribeError(Exception exception, string fallback)
        => exception is CategoryDataException data ? data.Message : fallback;

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
