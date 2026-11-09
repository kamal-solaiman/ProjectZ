using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class CategoryListItemViewModel : ObservableObject
{
    private int orderNumber;
    private string name;
    private bool isEditing;
    private string editingName;

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

    public ActionCommand BeginEditCommand { get; }
    public ActionCommand SaveEditCommand { get; }
    public ActionCommand CancelEditCommand { get; }
    public ActionCommand DeleteCommand { get; }

    public CategoryListItemViewModel(
        int orderNumber,
        string name,
        Action<CategoryListItemViewModel> onBeginEdit,
        Action<CategoryListItemViewModel> onSaveEdit,
        Action<CategoryListItemViewModel> onCancelEdit,
        Action<CategoryListItemViewModel> onDelete)
    {
        this.orderNumber = orderNumber;
        this.name = name;
        editingName = name;

        BeginEditCommand = new ActionCommand(() => onBeginEdit(this));
        SaveEditCommand = new ActionCommand(() => onSaveEdit(this));
        CancelEditCommand = new ActionCommand(() => onCancelEdit(this));
        DeleteCommand = new ActionCommand(() => onDelete(this));
    }
}

public sealed class CategoriesViewModel : ObservableObject
{
    private readonly ObservableCollection<string> sharedCategories;
    private readonly Action? navigateToAddProduct;

    private string newCategoryName = "";
    private string searchText = "";
    private string statusMessage = "مرحلة التصميم (بدون Backend): أضف أو عدّل أو احذف أسماء التصنيفات هنا لتظهر فورًا في قائمة التصنيفات بشاشة إضافة منتج.";
    private bool isErrorStatus;

    public ObservableCollection<CategoryListItemViewModel> FilteredCategories { get; } = [];

    public Action<ConfirmActionViewModel, Action>? OpenConfirmDialogRequested { get; set; }

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

    public int TotalCategoriesCount => sharedCategories.Count;
    public string TotalCountDisplay => $"{sharedCategories.Count} تصنيف";
    public bool HasNoCategories => FilteredCategories.Count == 0;
    public string EmptyStateText => string.IsNullOrWhiteSpace(SearchText)
        ? "لا توجد تصنيفات مسجلة حاليًا. أضف تصنيفًا جديدًا من الأعلى ليظهر في قائمة إضافة منتج."
        : "لا توجد تصنيفات مطابقة للبحث الحالي.";

    public ActionCommand AddCategoryCommand { get; }
    public ActionCommand ClearSearchCommand { get; }
    public ActionCommand GoToAddProductCommand { get; }

    public CategoriesViewModel()
        : this(PosDesignSeedData.CreateSharedCategories(), null)
    {
    }

    public CategoriesViewModel(
        ObservableCollection<string> sharedCategories,
        Action? navigateToAddProduct = null)
    {
        this.sharedCategories = sharedCategories;
        this.navigateToAddProduct = navigateToAddProduct;

        AddCategoryCommand = new ActionCommand(AddCategory);
        ClearSearchCommand = new ActionCommand(() => SearchText = "");
        GoToAddProductCommand = new ActionCommand(() => this.navigateToAddProduct?.Invoke());

        this.sharedCategories.CollectionChanged += OnSharedCategoriesChanged;
        RefreshFilteredCategories();
    }

    private void AddCategory()
    {
        var trimmed = NewCategoryName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            SetStatus("يرجى إدخال اسم التصنيف الجديد أولًا.", true);
            return;
        }

        var existing = sharedCategories.FirstOrDefault(c => c.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SetStatus($"التصنيف «{existing}» موجود بالفعل في القائمة.", true);
            return;
        }

        sharedCategories.Add(trimmed);
        NewCategoryName = "";
        SetStatus($"تمت إضافة التصنيف «{trimmed}» وسيظهر فورًا في قائمة التصنيفات بشاشة إضافة منتج.", false);
    }

    private void BeginEdit(CategoryListItemViewModel item)
    {
        foreach (var entry in FilteredCategories)
        {
            if (!ReferenceEquals(entry, item) && entry.IsEditing)
            {
                entry.IsEditing = false;
                entry.EditingName = entry.Name;
            }
        }

        item.EditingName = item.Name;
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

        if (trimmed == item.Name)
        {
            item.IsEditing = false;
            SetStatus($"لم يتم إجراء تغيير على التصنيف «{item.Name}».", false);
            return;
        }

        var currentIndex = sharedCategories.IndexOf(item.Name);
        if (currentIndex < 0)
        {
            item.IsEditing = false;
            RefreshFilteredCategories();
            return;
        }

        var duplicateExists = sharedCategories
            .Where((_, idx) => idx != currentIndex)
            .Any(c => c.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (duplicateExists)
        {
            SetStatus($"التصنيف «{trimmed}» موجود بالفعل في القائمة.", true);
            return;
        }

        var oldName = item.Name;
        item.IsEditing = false;
        sharedCategories[currentIndex] = trimmed;
        SetStatus($"تم تعديل التصنيف من «{oldName}» إلى «{trimmed}» وتحديث قائمة التصنيفات في إضافة منتج.", false);
    }

    private void CancelEdit(CategoryListItemViewModel item)
    {
        item.IsEditing = false;
        item.EditingName = item.Name;
    }

    private void RequestDelete(CategoryListItemViewModel item)
    {
        var categoryName = item.Name;
        var confirmVm = new ConfirmActionViewModel(
            "تأكيد حذف التصنيف",
            categoryName,
            $"هل تريد حذف التصنيف «{categoryName}»؟ سيتم إزالته من قائمة التصنيفات (ComboBox) في شاشة إضافة منتج.",
            "حذف التصنيف");

        if (OpenConfirmDialogRequested is not null)
        {
            OpenConfirmDialogRequested(confirmVm, () => DeleteConfirmed(categoryName));
        }
        else
        {
            DeleteConfirmed(categoryName);
        }
    }

    private void DeleteConfirmed(string categoryName)
    {
        if (!sharedCategories.Remove(categoryName))
            return;

        SetStatus($"تم حذف التصنيف «{categoryName}» من القائمة.", false);
    }

    private void OnSharedCategoriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Changed(nameof(TotalCategoriesCount));
        Changed(nameof(TotalCountDisplay));
        RefreshFilteredCategories();
    }

    private void RefreshFilteredCategories()
    {
        var query = searchText.Trim();
        FilteredCategories.Clear();

        for (var i = 0; i < sharedCategories.Count; i++)
        {
            var category = sharedCategories[i];
            if (!string.IsNullOrEmpty(query)
                && !category.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            FilteredCategories.Add(new CategoryListItemViewModel(
                i + 1,
                category,
                BeginEdit,
                SaveEdit,
                CancelEdit,
                RequestDelete));
        }

        Changed(nameof(TotalCategoriesCount));
        Changed(nameof(TotalCountDisplay));
        Changed(nameof(HasNoCategories));
        Changed(nameof(EmptyStateText));
    }

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
