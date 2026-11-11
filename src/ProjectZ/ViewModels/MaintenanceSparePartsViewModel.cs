using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class MaintenanceSparePartsViewModel : ObservableObject
{
    private readonly ObservableCollection<MaintenanceSparePartItem> sharedSpareParts;
    private readonly Action? navigateToActions;
    private readonly Action? notifySparePartsChanged;

    private MaintenanceSparePartItem? editingPart;
    private string partName = "";
    private string selectedCategory = "شاشات";
    private string customCategoryName = "";
    private string quantityText = "1";
    private string costPriceText = "0";
    private string sellPriceText = "0";

    private string searchText = "";
    private string selectedCategoryFilter = "كل الفئات";
    private string statusMessage = "شاشة تسجيل قطع الغيار: خاصة بمخزون قطع غيار الصيانة فقط (منفصلة تمامًا عن مخزون المبيعات العام).";
    private bool isErrorStatus;

    public ObservableCollection<MaintenanceSparePartItem> FilteredSpareParts { get; } = [];
    public ObservableCollection<string> CategoryOptions { get; } = [];
    public ObservableCollection<string> CategoryFilterOptions { get; } = [];

    public Action<ConfirmActionViewModel, Action>? OpenConfirmDialogRequested { get; set; }

    public bool IsEditMode => editingPart is not null;
    public bool IsAddMode => editingPart is null;
    public string FormHeaderTitle => IsEditMode
        ? $"تعديل قطعة الغيار ({editingPart!.CodeDisplay})"
        : "تسجيل قطعة غيار جديدة للصيانة";
    public string SaveButtonText => IsEditMode ? "حفظ التعديلات" : "+ تسجيل قطعة الغيار";

    public string PartName
    {
        get => partName;
        set
        {
            if (partName == value) return;
            partName = value;
            Changed();
        }
    }

    public string SelectedCategory
    {
        get => selectedCategory;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || selectedCategory == value) return;
            selectedCategory = value;
            Changed();
            Changed(nameof(IsCustomCategorySelected));
        }
    }

    public bool IsCustomCategorySelected => SelectedCategory == "أخرى";

    public string CustomCategoryName
    {
        get => customCategoryName;
        set
        {
            if (customCategoryName == value) return;
            customCategoryName = value;
            Changed();
        }
    }

    public string QuantityText
    {
        get => quantityText;
        set
        {
            if (quantityText == value) return;
            quantityText = value;
            Changed();
        }
    }

    public string CostPriceText
    {
        get => costPriceText;
        set
        {
            if (costPriceText == value) return;
            costPriceText = value;
            Changed();
            Changed(nameof(PreviewProfitDisplay));
        }
    }

    public string SellPriceText
    {
        get => sellPriceText;
        set
        {
            if (sellPriceText == value) return;
            sellPriceText = value;
            Changed();
            Changed(nameof(PreviewProfitDisplay));
        }
    }

    public string PreviewProfitDisplay
    {
        get
        {
            var cost = PosFormatting.TryParseAmount(CostPriceText, out var c) && c >= 0m ? c : 0m;
            var sell = PosFormatting.TryParseAmount(SellPriceText, out var s) && s >= 0m ? s : 0m;
            return PosFormatting.FormatCurrency(Math.Max(0m, sell - cost));
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
            RefreshFilteredSpareParts();
        }
    }

    public string SelectedCategoryFilter
    {
        get => selectedCategoryFilter;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || selectedCategoryFilter == value) return;
            selectedCategoryFilter = value;
            Changed();
            RefreshFilteredSpareParts();
        }
    }

    public bool HasNoFilteredSpareParts => FilteredSpareParts.Count == 0;

    // Summary KPIs
    public string TotalPartsCountDisplay => $"{sharedSpareParts.Count} صنف";
    public string TotalQuantityUnitsDisplay => $"{sharedSpareParts.Sum(p => p.Quantity)} قطعة";
    public string TotalCostValueDisplay => PosFormatting.FormatCurrency(sharedSpareParts.Sum(p => p.TotalCostValue));
    public string LowOrOutCountDisplay => $"{sharedSpareParts.Count(p => p.IsOutOfStock || p.IsLowStock)} صنف";

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

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public ActionCommand GoToActionsCommand { get; }
    public ActionCommand SavePartCommand { get; }
    public ActionCommand CancelEditCommand { get; }
    public ActionCommand ResetFormCommand { get; }
    public ActionCommand ClearSearchCommand { get; }
    public ActionCommand ClearFiltersCommand { get; }
    public RelayCommand<MaintenanceSparePartItem> BeginEditCommand { get; }
    public RelayCommand<MaintenanceSparePartItem> DeletePartCommand { get; }

    public MaintenanceSparePartsViewModel()
        : this(MaintenanceDesignSeedData.CreateSharedSampleSpareParts(), null, null)
    {
    }

    public MaintenanceSparePartsViewModel(
        ObservableCollection<MaintenanceSparePartItem> sharedSpareParts,
        Action? navigateToActions = null,
        Action? notifySparePartsChanged = null)
    {
        this.sharedSpareParts = sharedSpareParts;
        this.navigateToActions = navigateToActions;
        this.notifySparePartsChanged = notifySparePartsChanged;

        foreach (var cat in MaintenanceDesignSeedData.SparePartCategories)
            CategoryOptions.Add(cat);

        this.sharedSpareParts.CollectionChanged += OnSparePartsChanged;

        GoToActionsCommand = new ActionCommand(() => this.navigateToActions?.Invoke());
        SavePartCommand = new ActionCommand(SavePart);
        CancelEditCommand = new ActionCommand(CancelEdit);
        ResetFormCommand = new ActionCommand(() =>
        {
            CancelEdit();
            SetStatus("تم تفريغ الحقول لبدء تسجيل قطعة غيار جديدة.", false);
        });
        ClearSearchCommand = new ActionCommand(() => SearchText = "");
        ClearFiltersCommand = new ActionCommand(() =>
        {
            searchText = "";
            selectedCategoryFilter = "كل الفئات";
            Changed(nameof(SearchText));
            Changed(nameof(SelectedCategoryFilter));
            RefreshFilteredSpareParts();
        });
        BeginEditCommand = new RelayCommand<MaintenanceSparePartItem>(BeginEdit);
        DeletePartCommand = new RelayCommand<MaintenanceSparePartItem>(RequestDelete);

        RebuildCategoryFilterOptions();
        RefreshFilteredSpareParts();
    }

    public void NotifyExternalDataChanged()
    {
        RebuildCategoryFilterOptions();
        RefreshFilteredSpareParts();
        NotifySummaryKpis();
    }

    private void OnSparePartsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildCategoryFilterOptions();
        RefreshFilteredSpareParts();
        NotifySummaryKpis();
    }

    private void SavePart()
    {
        var trimmedName = PartName.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            SetStatus("أدخل اسم قطعة الغيار أولًا.", true);
            return;
        }

        var category = IsCustomCategorySelected ? CustomCategoryName.Trim() : SelectedCategory;
        if (string.IsNullOrWhiteSpace(category))
        {
            SetStatus("اختر الفئة أو أدخل اسم الفئة المخصصة.", true);
            return;
        }

        if (!PosFormatting.TryParseNonNegativeInt(QuantityText, out var qty))
        {
            SetStatus("أدخل كمية صحيحة (صفر أو أكثر).", true);
            return;
        }

        if (!PosFormatting.TryParseAmount(CostPriceText, out var cost) || cost < 0m)
        {
            SetStatus("أدخل سعر تكلفة صحيحًا.", true);
            return;
        }

        if (!PosFormatting.TryParseAmount(SellPriceText, out var sell) || sell < 0m)
        {
            SetStatus("أدخل سعر تركيب صحيحًا للعميل.", true);
            return;
        }

        if (!CategoryOptions.Contains(category))
        {
            var otherIdx = CategoryOptions.IndexOf("أخرى");
            if (otherIdx >= 0)
                CategoryOptions.Insert(otherIdx, category);
            else
                CategoryOptions.Add(category);
        }

        if (editingPart is not null)
        {
            var target = editingPart;
            var duplicate = sharedSpareParts.FirstOrDefault(p =>
                !ReferenceEquals(p, target)
                && p.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase));
            if (duplicate is not null)
            {
                SetStatus($"توجد قطعة غيار أخرى مسجلة بنفس الاسم «{duplicate.Name}».", true);
                return;
            }

            target.Name = trimmedName;
            target.Category = category;
            target.Quantity = qty;
            target.CostPrice = cost;
            target.SellPrice = sell;

            var editedCode = target.CodeDisplay;
            ClearFormFields();
            RebuildCategoryFilterOptions();
            RefreshFilteredSpareParts();
            NotifySummaryKpis();
            notifySparePartsChanged?.Invoke();

            SetStatus($"تم حفظ تعديلات قطعة الغيار «{trimmedName}» ({editedCode}) ومزامنتها مع إجراءات الصيانة.", false);
            return;
        }

        var existing = sharedSpareParts.FirstOrDefault(p =>
            p.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SetStatus($"قطعة الغيار «{existing.Name}» مسجلة بالفعل ({existing.CodeDisplay})؛ يمكنك الضغط على «تعديل» لتحديث كميتها أو سعرها.", true);
            return;
        }

        var nextId = sharedSpareParts.Count == 0 ? 1 : sharedSpareParts.Max(p => p.Id) + 1;
        var newPart = new MaintenanceSparePartItem
        {
            Id = nextId,
            Name = trimmedName,
            Category = category,
            Quantity = qty,
            CostPrice = cost,
            SellPrice = sell
        };

        sharedSpareParts.Insert(0, newPart);
        ClearFormFields();
        notifySparePartsChanged?.Invoke();

        SetStatus($"تم تسجيل قطعة الغيار «{newPart.Name}» ({newPart.CodeDisplay}) بنجاح وستظهر فورًا في صرف قطع الغيار بإجراءات الصيانة.", false);
    }

    private void BeginEdit(MaintenanceSparePartItem? item)
    {
        if (item is null) return;

        foreach (var part in sharedSpareParts)
            part.IsSelected = ReferenceEquals(part, item);

        editingPart = item;
        partName = item.Name;
        if (CategoryOptions.Contains(item.Category))
        {
            selectedCategory = item.Category;
            customCategoryName = "";
        }
        else
        {
            selectedCategory = "أخرى";
            customCategoryName = item.Category;
        }
        quantityText = item.Quantity.ToString();
        costPriceText = PosFormatting.FormatPlainNumber(item.CostPrice);
        sellPriceText = PosFormatting.FormatPlainNumber(item.SellPrice);

        Changed(nameof(IsEditMode));
        Changed(nameof(IsAddMode));
        Changed(nameof(FormHeaderTitle));
        Changed(nameof(SaveButtonText));
        Changed(nameof(PartName));
        Changed(nameof(SelectedCategory));
        Changed(nameof(IsCustomCategorySelected));
        Changed(nameof(CustomCategoryName));
        Changed(nameof(QuantityText));
        Changed(nameof(CostPriceText));
        Changed(nameof(SellPriceText));
        Changed(nameof(PreviewProfitDisplay));

        SetStatus($"وضع التعديل: جاري تعديل «{item.Name}» ({item.CodeDisplay}).", false);
    }

    private void CancelEdit()
    {
        ClearFormFields();
    }

    private void ClearFormFields()
    {
        foreach (var part in sharedSpareParts)
            part.IsSelected = false;

        editingPart = null;
        partName = "";
        selectedCategory = CategoryOptions.FirstOrDefault() ?? "شاشات";
        customCategoryName = "";
        quantityText = "1";
        costPriceText = "0";
        sellPriceText = "0";

        Changed(nameof(IsEditMode));
        Changed(nameof(IsAddMode));
        Changed(nameof(FormHeaderTitle));
        Changed(nameof(SaveButtonText));
        Changed(nameof(PartName));
        Changed(nameof(SelectedCategory));
        Changed(nameof(IsCustomCategorySelected));
        Changed(nameof(CustomCategoryName));
        Changed(nameof(QuantityText));
        Changed(nameof(CostPriceText));
        Changed(nameof(SellPriceText));
        Changed(nameof(PreviewProfitDisplay));
    }

    private void RequestDelete(MaintenanceSparePartItem? item)
    {
        if (item is null) return;

        var confirmVm = new ConfirmActionViewModel(
            "تأكيد حذف قطعة الغيار",
            $"{item.Name} ({item.CodeDisplay})",
            $"هل تريد حذف قطعة الغيار «{item.Name}» من مخزون قطع غيار الصيانة؟ لن تتأثر الفواتير السابقة التي صُرفت عليها هذه القطعة.",
            "حذف القطعة");

        if (OpenConfirmDialogRequested is not null)
        {
            OpenConfirmDialogRequested(confirmVm, () => DeleteConfirmed(item));
        }
        else
        {
            DeleteConfirmed(item);
        }
    }

    private void DeleteConfirmed(MaintenanceSparePartItem item)
    {
        if (ReferenceEquals(editingPart, item))
            ClearFormFields();

        if (!sharedSpareParts.Remove(item))
            return;

        notifySparePartsChanged?.Invoke();
        SetStatus($"تم حذف قطعة الغيار «{item.Name}» من مخزون الصيانة.", false);
    }

    private void RebuildCategoryFilterOptions()
    {
        var previous = SelectedCategoryFilter;
        var categories = CategoryOptions
            .Where(c => c != "أخرى")
            .Concat(sharedSpareParts.Select(p => p.Category))
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        CategoryFilterOptions.Clear();
        CategoryFilterOptions.Add("كل الفئات");
        foreach (var cat in categories)
            CategoryFilterOptions.Add(cat);
        CategoryFilterOptions.Add("نفد أو رصيد منخفض");

        if (CategoryFilterOptions.Contains(previous))
            selectedCategoryFilter = previous;
        else
            selectedCategoryFilter = "كل الفئات";

        Changed(nameof(SelectedCategoryFilter));
    }

    private void RefreshFilteredSpareParts()
    {
        var query = SearchText.Trim();
        FilteredSpareParts.Clear();

        foreach (var part in sharedSpareParts)
        {
            if (SelectedCategoryFilter == "نفد أو رصيد منخفض")
            {
                if (!part.IsOutOfStock && !part.IsLowStock) continue;
            }
            else if (SelectedCategoryFilter != "كل الفئات"
                && !string.Equals(part.Category, SelectedCategoryFilter, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(query))
            {
                var hit = part.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || part.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                          || part.CodeDisplay.Contains(query, StringComparison.OrdinalIgnoreCase);
                if (!hit) continue;
            }

            FilteredSpareParts.Add(part);
        }

        Changed(nameof(HasNoFilteredSpareParts));
    }

    private void NotifySummaryKpis()
    {
        Changed(nameof(TotalPartsCountDisplay));
        Changed(nameof(TotalQuantityUnitsDisplay));
        Changed(nameof(TotalCostValueDisplay));
        Changed(nameof(LowOrOutCountDisplay));
    }

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }
}
