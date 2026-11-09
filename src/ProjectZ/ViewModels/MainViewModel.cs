using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly UserSession user;
    private NavigationMenuItemViewModel? selectedItem;
    private bool isSidebarCollapsed;

    public IReadOnlyList<NavigationMenuCategoryViewModel> Categories { get; }
    public string Welcome => $"مرحبًا، {user.DisplayName}";
    public string Username => user.Username;
    public string SelectedCategoryTitle => selectedItem?.Category.Title ?? "مساحة العمل";
    public string SelectedPageTitle => selectedItem?.Title ?? "مرحبًا بك في ProjectZ";
    public string SelectedPageDescription => selectedItem is null
        ? "اختر إحدى الوظائف من القائمة الجانبية."
        : "هذا العنصر للتحديد فقط حاليًا؛ لم تُنفذ شاشته بعد.";
    public RelayCommand LogoutCommand { get; }

    // Collapsed = narrow icon rail anchored to the right edge; expanded = full menu.
    public bool IsSidebarCollapsed
    {
        get => isSidebarCollapsed;
        private set
        {
            if (isSidebarCollapsed == value) return;

            isSidebarCollapsed = value;
            Changed();
        }
    }
    public RelayCommand ToggleSidebarCommand { get; }

    public MainViewModel(UserSession user, Action logout)
    {
        this.user = user;
        LogoutCommand = new RelayCommand(logout);
        ToggleSidebarCommand = new RelayCommand(() => IsSidebarCollapsed = !IsSidebarCollapsed);
        Categories = new[]
        {
            new NavigationMenuCategoryViewModel("المبيعات", "\U0001F6D2",
                "نقطة البيع", "اضافة منتج", "المخزون", "التصنيفات", "الفواتير", "المرتجع والاستبدال"),
            new NavigationMenuCategoryViewModel("المشتريات", "\U0001F4E6",
                "شراء فاتورة", "فاتورة المشتريات"),
            new NavigationMenuCategoryViewModel("الصيانة", "\U0001F527",
                "امر صيانة", "فواتير الصيانة", "اجراءات الصيانة", "قطع الغيار"),
            new NavigationMenuCategoryViewModel("المالية", "\U0001F4B0",
                "تحويل فودافون كاش", "تحويل انستاباي", "سجلات فودافون كاش", "سجلات انستاباي", "حساباتي"),
            new NavigationMenuCategoryViewModel("العملاء والموردين", "\U0001F465",
                "العملاء", "الموردين"),
            new NavigationMenuCategoryViewModel("المستخدمين", "\U0001F464",
                "المستخدمين", "الصلاحيات"),
            new NavigationMenuCategoryViewModel("الاعدادات", "\u2699",
                "اعدادات عامة", "المظهر", "النسخ الاحتياطي")
        };

        // Start at the first available destination; all other categories stay closed.
        SelectItem(Categories[0].Items[0]);
    }

    public void ToggleCategory(NavigationMenuCategoryViewModel category)
    {
        if (!Categories.Contains(category)) return;

        var expand = !category.IsExpanded;
        // A category header only controls disclosure; navigation changes on item selection.
        foreach (var itemCategory in Categories)
            itemCategory.SetExpanded(expand && ReferenceEquals(itemCategory, category));
    }

    // From the collapsed rail a category icon expands the sidebar and opens that category.
    public void OpenCategoryFromRail(NavigationMenuCategoryViewModel category)
    {
        if (!Categories.Contains(category)) return;

        IsSidebarCollapsed = false;
        foreach (var other in Categories)
            other.SetExpanded(ReferenceEquals(other, category));
    }

    public void SelectItem(NavigationMenuItemViewModel item)
    {
        if (!Categories.Contains(item.Category) || !item.Category.Items.Contains(item)) return;

        foreach (var category in Categories)
        {
            category.SetExpanded(ReferenceEquals(category, item.Category));
            foreach (var categoryItem in category.Items)
                categoryItem.SetSelected(ReferenceEquals(categoryItem, item));
        }

        selectedItem = item;
        Changed(nameof(SelectedCategoryTitle));
        Changed(nameof(SelectedPageTitle));
        Changed(nameof(SelectedPageDescription));
    }
}

public sealed class NavigationMenuCategoryViewModel : ObservableObject
{
    private bool isExpanded;

    public string Title { get; }
    public string Icon { get; }
    public IReadOnlyList<NavigationMenuItemViewModel> Items { get; }
    public bool IsExpanded => isExpanded;
    // Physical glyphs (the chevron TextBlock is LTR): closed arrow points left, toward the RTL content.
    public string Chevron => IsExpanded ? "⌄" : "‹";

    public NavigationMenuCategoryViewModel(string title, string icon, params string[] itemTitles)
    {
        Title = title;
        Icon = icon;
        Items = itemTitles.Select(itemTitle => new NavigationMenuItemViewModel(this, itemTitle)).ToArray();
    }

    internal void SetExpanded(bool value)
    {
        if (isExpanded == value) return;

        isExpanded = value;
        Changed(nameof(IsExpanded));
        Changed(nameof(Chevron));
    }
}

public sealed class NavigationMenuItemViewModel : ObservableObject
{
    private bool isSelected;

    public NavigationMenuCategoryViewModel Category { get; }
    public string Title { get; }
    public bool IsSelected => isSelected;

    internal NavigationMenuItemViewModel(NavigationMenuCategoryViewModel category, string title)
    {
        Category = category;
        Title = title;
    }

    internal void SetSelected(bool value)
    {
        if (isSelected == value) return;

        isSelected = value;
        Changed(nameof(IsSelected));
    }
}
