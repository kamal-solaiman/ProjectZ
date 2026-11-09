using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly UserSession user;
    private NavigationMenuItemViewModel? selectedItem;

    public IReadOnlyList<NavigationMenuCategoryViewModel> Categories { get; }
    public string Welcome => $"مرحبًا، {user.DisplayName}";
    public string Username => user.Username;
    public string SelectedCategoryTitle => selectedItem?.Category.Title ?? "مساحة العمل";
    public string SelectedPageTitle => selectedItem?.Title ?? "مرحبًا بك في ProjectZ";
    public string SelectedPageDescription => selectedItem is null
        ? "اختر إحدى الوظائف من القائمة الجانبية."
        : "هذا العنصر للتحديد فقط حاليًا؛ لم تُنفذ شاشته بعد.";
    public RelayCommand LogoutCommand { get; }

    public MainViewModel(UserSession user, Action logout)
    {
        this.user = user;
        LogoutCommand = new RelayCommand(logout);
        Categories = new[]
        {
            new NavigationMenuCategoryViewModel("المبيعات",
                "نقطة البيع", "اضافة منتج", "المخزون", "التصنيفات", "الفواتير", "المرتجع والاستبدال"),
            new NavigationMenuCategoryViewModel("المشتريات",
                "شراء فاتورة", "فاتورة المشتريات"),
            new NavigationMenuCategoryViewModel("الصيانة",
                "امر صيانة", "فواتير الصيانة", "اجراءات الصيانة", "قطع الغيار"),
            new NavigationMenuCategoryViewModel("المالية",
                "تحويل فودافون كاش", "تحويل انستاباي", "سجلات فودافون كاش", "سجلات انستاباي", "حساباتي"),
            new NavigationMenuCategoryViewModel("العملاء والموردين",
                "العملاء", "الموردين"),
            new NavigationMenuCategoryViewModel("المستخدمين",
                "المستخدمين", "الصلاحيات"),
            new NavigationMenuCategoryViewModel("الاعدادات",
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
    public IReadOnlyList<NavigationMenuItemViewModel> Items { get; }
    public bool IsExpanded => isExpanded;
    public string Chevron => IsExpanded ? "⌄" : "›";

    public NavigationMenuCategoryViewModel(string title, params string[] itemTitles)
    {
        Title = title;
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
