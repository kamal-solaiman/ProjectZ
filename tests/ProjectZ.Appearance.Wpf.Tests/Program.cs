using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ProjectZ.Models;
using ProjectZ.Services;
using ProjectZ.ViewModels;
using ProjectZ.Views;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new ProjectZ.App();
        app.InitializeComponent();
        var runtime = ThemeManager.Instance;
        runtime.Initialize();

        static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            Console.WriteLine("PASS: " + name);
        }

        Check(Application.Current.Resources["Theme.Brush.Surface"] is Brush, "Application resource graph loads before any window");
        var studio = new AppearanceView { DataContext = new AppearanceViewModel(runtime) };
        var login = new LoginWindow();
        var main = new ProjectZ.Views.MainWindow();
        var picker = new ColorPickerWindow("#147D92");
        foreach (var theme in ThemeCatalog.All)
        {
            runtime.Preview(new AppearanceSettings { ThemeId = theme.Id, CustomPalette = ThemeCatalog.CustomPalette, CornerRadius = theme.CornerRadius, SurfaceOpacity = theme.SurfaceOpacity });
            foreach (var key in ThemeManager.ColorKeys)
            {
                if (Application.Current.Resources["Theme.Color." + key] is not Color)
                    throw new Exception("Missing live color: " + key);
                if (Application.Current.Resources["Theme.Brush." + key] is not Brush)
                    throw new Exception("Missing live brush: " + key);
            }
            studio.Measure(new Size(980, 760));
            studio.Arrange(new Rect(0, 0, 980, 760));
            studio.UpdateLayout();
            login.Measure(new Size(960, 640));
            login.Arrange(new Rect(0, 0, 960, 640));
            main.Measure(new Size(1320, 760));
            main.Arrange(new Rect(0, 0, 1320, 760));
            picker.Measure(new Size(520, 590));
            picker.Arrange(new Rect(0, 0, 520, 590));
            Check(true, "WPF layout and live resources apply: " + theme.Id);
        }
        // Regression: navigating to المظهر must notify IsAppearanceSelected so the host Grid
        // leaves Collapsed. A missing notification rendered the content area empty/white.
        var mainVm = new MainViewModel(new UserSession(0, "smoke", "اختبار الدخان"), () => { },
            appearance: new AppearanceViewModel(runtime));
        main.DataContext = mainVm;
        PumpDispatcher(); // let DataContext inheritance and style-trigger bindings attach
        // New sidebar menus: «التقارير» sits under «العملاء والموردين» and above «المستخدمين», and
        // «المصروفات» sits under «التقارير» and above «المستخدمين». They are navigation only in
        // this stage: selecting an item keeps the placeholder workspace instead of an empty panel.
        Check(mainVm.Categories.Select(category => category.Title).SequenceEqual(new[]
            {
                "المبيعات", "المشتريات", "الصيانة", "المالية", "العملاء والموردين",
                "التقارير", "المصروفات", "المستخدمين", "الاعدادات"
            }),
            "Sidebar order puts التقارير and المصروفات between العملاء والموردين and المستخدمين");
        var reportsMenu = mainVm.Categories.Single(category => category.Title == "التقارير");
        Check(reportsMenu.Items.Select(item => item.Title).SequenceEqual(new[]
            {
                "تقارير المبيعات", "تقارير المشتريات", "تقارير الصيانة",
                "تقارير المالية", "تقارير المصروفات", "اجمالي التقارير"
            }),
            "Reports menu lists the six report windows");
        var expensesMenu = mainVm.Categories.Single(category => category.Title == "المصروفات");
        Check(expensesMenu.Items.Select(item => item.Title).SequenceEqual(new[]
            {
                "اضافة مصروف", "فواتير المصروفات", "تصنيفات المصروفات"
            }),
            "Expenses menu lists the three expense windows");
        var salesReportsItem = mainVm.Categories.Single(category => category.Title == "التقارير")
            .Items.Single(item => item.Title == "تقارير المبيعات");
        mainVm.SelectItem(salesReportsItem);
        Check(!mainVm.IsImplementedScreenSelected
            && mainVm.SelectedCategoryTitle == "التقارير"
            && mainVm.SelectedPageTitle == "تقارير المبيعات",
            "Menus-only items keep the placeholder workspace and never claim an implemented screen");
        // «فواتير المصروفات» lists the stored expenses with their split payments. In the design
        // preview it must show the five sample rows, keep them reachable through the filters and
        // hand a details view model to the window.
        var expenseInvoicesItem = mainVm.Categories.Single(c => c.Title == "المصروفات")
            .Items.Single(i => i.Title == "فواتير المصروفات");
        mainVm.SelectItem(expenseInvoicesItem);
        PumpDispatcher();
        Check(mainVm.IsExpenseInvoicesSelected && mainVm.IsImplementedScreenSelected,
            "SelectItem activates the expense-invoices screen as an implemented screen");
        Check(mainVm.ExpenseInvoices.FilteredInvoices.Count == 5
              && mainVm.ExpenseInvoices.ShownCount == 5,
            "The expense-invoices screen starts with the five sample expenses in design mode");
        Check(mainVm.ExpenseInvoices.ShownTotalAmount == 16455.5m
              && mainVm.ExpenseInvoices.SplitCount == 2,
            "The expense-invoices totals and split count are computed from the listed rows");
        var hostedExpenseInvoices = FindLogicalDescendant<ExpenseInvoicesView>(main);
        Check(hostedExpenseInvoices is not null, "MainWindow hosts ExpenseInvoicesView in its logical tree");
        Check(hostedExpenseInvoices!.DataContext is ExpenseInvoicesViewModel,
            "ExpenseInvoicesView receives the ExpenseInvoices view model");
        Check(((Grid)hostedExpenseInvoices.Parent).Visibility == Visibility.Visible,
            "Expense-invoices host Grid is Visible after navigating to فواتير المصروفات");

        mainVm.ExpenseInvoices.SearchText = "كهرباء";
        Check(mainVm.ExpenseInvoices.FilteredInvoices.Count == 1
              && mainVm.ExpenseInvoices.FilteredInvoices[0].CategoryName == "فواتير",
            "The free-text filter narrows the register to the matching expense");
        mainVm.ExpenseInvoices.SearchText = "";
        mainVm.ExpenseInvoices.SelectedCategoryFilter = "إيجار";
        Check(mainVm.ExpenseInvoices.FilteredInvoices.Count == 1,
            "The section filter narrows the register");
        mainVm.ExpenseInvoices.SelectedCategoryFilter = ExpenseInvoicesViewModel.AllCategories;
        mainVm.ExpenseInvoices.SelectedPaymentFilter = ExpenseInvoicesViewModel.SplitPayment;
        Check(mainVm.ExpenseInvoices.FilteredInvoices.Count == 2
              && mainVm.ExpenseInvoices.FilteredInvoices.All(invoice => invoice.IsSplitPayment),
            "The payment filter keeps only the expenses split across accounts");
        mainVm.ExpenseInvoices.SelectDatePresetCommand.Execute(ExpenseInvoicesViewModel.TodayPreset);
        mainVm.ExpenseInvoices.ClearFiltersCommand.Execute(null);
        Check(mainVm.ExpenseInvoices.FilteredInvoices.Count == 5 && mainVm.ExpenseInvoices.IsAllDatePreset,
            "Clearing the filters restores every listed expense");
        ExpenseInvoiceDetailsViewModel? openedDetails = null;
        mainVm.ExpenseInvoices.OpenDetailsDialogRequested = details => openedDetails = details;
        mainVm.ExpenseInvoices.OpenDetailsCommand.Execute(mainVm.ExpenseInvoices.FilteredInvoices
            .First(invoice => invoice.IsSplitPayment));
        Check(openedDetails is not null
              && openedDetails.Payments.Count == 2
              && openedDetails.IsPaymentTotalMatchingAmount,
            "Opening details hands a view model whose payment lines add up to the expense amount");

        // «تصنيفات المصروفات» is the first implemented expense screen. Selecting it must activate
        // its host Grid and load the six starter sections without SQL (design mode).
        var expenseCategoriesItem = mainVm.Categories.Single(c => c.Title == "المصروفات")
            .Items.Single(i => i.Title == "تصنيفات المصروفات");
        mainVm.SelectItem(expenseCategoriesItem);
        PumpDispatcher();
        Check(mainVm.IsExpenseCategoriesSelected, "SelectItem activates the expense categories screen");
        Check(mainVm.ExpenseCategories.FilteredCategories.Count == 6,
            "The expense categories screen starts with the six starter sections in design mode");
        var hostedExpenseCategories = FindLogicalDescendant<ExpenseCategoriesView>(main);
        Check(hostedExpenseCategories is not null, "MainWindow hosts ExpenseCategoriesView in its logical tree");
        Check(hostedExpenseCategories!.DataContext is ExpenseCategoriesViewModel,
            "ExpenseCategoriesView receives the ExpenseCategories view model");
        Check(((Grid)hostedExpenseCategories.Parent).Visibility == Visibility.Visible,
            "Expense categories host Grid is Visible after navigating to تصنيفات المصروفات");

        // «اضافة مصروف» reads its sections from dbo.ExpenseCategories; the design preview must
        // therefore start with the same six starter sections and the sample accounts, and a save
        // must deduct the chosen account in the preview only.
        var addExpenseItem = mainVm.Categories.Single(c => c.Title == "المصروفات")
            .Items.Single(i => i.Title == "اضافة مصروف");
        mainVm.SelectItem(addExpenseItem);
        PumpDispatcher();
        Check(mainVm.IsAddExpenseSelected, "SelectItem activates the add-expense screen");
        Check(mainVm.AddExpense.Categories.Count == 6,
            "The add-expense category picker shows the six starter sections in design mode");
        Check(mainVm.AddExpense.Accounts.Count > 0
              && mainVm.AddExpense.Payments.Count == 1
              && mainVm.AddExpense.Payments[0].SelectedAccount is not null,
            "The add-expense screen starts with one payment line on an active account");
        var hostedAddExpense = FindLogicalDescendant<AddExpenseView>(main);
        Check(hostedAddExpense is not null, "MainWindow hosts AddExpenseView in its logical tree");
        Check(hostedAddExpense!.DataContext is AddExpenseViewModel,
            "AddExpenseView receives the AddExpense view model");
        Check(((Grid)hostedAddExpense.Parent).Visibility == Visibility.Visible,
            "Add-expense host Grid is Visible after navigating to اضافة مصروف");
        var firstAccount = mainVm.AddExpense.Payments[0].SelectedAccount!;
        var balanceBeforeExpense = firstAccount.Balance;
        mainVm.AddExpense.AmountText = "250.25";
        Check(mainVm.AddExpense.Payments[0].AmountValue == 250.25m && mainVm.AddExpense.IsAllocationBalanced,
            "A single payment line takes the whole amount without typing it twice");
        mainVm.AddExpense.DescriptionText = "اختبار الدخان";
        mainVm.AddExpense.SaveCommand.Execute(null);
        Check(mainVm.AddExpense.HasLastSaved && mainVm.AddExpense.LastSavedNumber.StartsWith("EXP-"),
            "A design-mode expense save produces an EXP- document number");
        Check(!mainVm.AddExpense.IsErrorStatus,
            "A valid design-mode expense save reports success, not an error");
        Check(firstAccount.Balance == balanceBeforeExpense - 250.25m,
            "A design-mode expense save deducts the selected account balance");

        // Split payment: adding a second line halves the amount so it stays valid, then the save
        // must deduct both accounts and keep one ledger row per account.
        mainVm.AddExpense.AmountText = "300.00";
        mainVm.AddExpense.AddPaymentCommand.Execute(null);
        Check(mainVm.AddExpense.Payments.Count == 2 && mainVm.AddExpense.HasMultiplePayments,
            "AddPaymentCommand adds a second payment line");
        Check(mainVm.AddExpense.IsAllocationBalanced && !mainVm.AddExpense.HasAllocationGap,
            "Adding a payment line keeps the split equal to the expense amount");
        var splitFirst = mainVm.AddExpense.Payments[0].SelectedAccount!;
        var splitSecond = mainVm.AddExpense.Payments[1].SelectedAccount!;
        var firstBefore = splitFirst.Balance;
        var secondBefore = splitSecond.Balance;
        Check(!ReferenceEquals(splitFirst, splitSecond),
            "The second payment line starts on a different account");
        mainVm.AddExpense.SaveCommand.Execute(null);
        Check(mainVm.AddExpense.Payments.Count == 1,
            "After a split save the form returns to one payment line for the next expense");
        Check(splitFirst.Balance == firstBefore - 150m && splitSecond.Balance == secondBefore - 150m,
            "A split expense deducts every payment account by its own share");
        var notified = new List<string?>();
        mainVm.PropertyChanged += (_, args) => notified.Add(args.PropertyName);
        // Items are looked up by their Arabic labels instead of a positional index, so inserting
        // a category in the sidebar (التقارير/المصروفات) cannot silently retarget this test.
        var settingsCategory = mainVm.Categories.Single(category => category.Title == "الاعدادات");
        var appearanceItem = settingsCategory.Items.Single(item => item.Title == "المظهر");
        mainVm.SelectItem(appearanceItem);
        PumpDispatcher(); // let the DataTrigger binding consume the PropertyChanged event
        Check(mainVm.IsAppearanceSelected, "SelectItem activates the appearance screen");
        Check(notified.Contains(nameof(MainViewModel.IsAppearanceSelected)),
            "SelectItem notifies IsAppearanceSelected so the visibility trigger re-evaluates");
        // An unshown Window never applies its template, so walk the logical tree (fully built
        // from XAML) instead of the visual tree, then lay out the host Grid directly.
        var hostedStudio = FindLogicalDescendant<AppearanceView>(main);
        Check(hostedStudio is not null, "MainWindow hosts AppearanceView in its logical tree");
        Check(hostedStudio!.Parent is Grid, "AppearanceView sits inside its visibility-trigger host Grid");
        var hostGrid = (Grid)hostedStudio.Parent;
        Check(ReferenceEquals(hostGrid.DataContext, mainVm), "Window DataContext inherits down to the appearance host Grid");
        Check(hostGrid.Visibility == Visibility.Visible,
            "Appearance host Grid is Visible after navigating to المظهر (DataTrigger re-evaluated)");
        hostGrid.Measure(new Size(1100, 700));
        hostGrid.Arrange(new Rect(0, 0, 1100, 700));
        hostGrid.UpdateLayout();
        Check(hostedStudio.ActualWidth > 100 && hostedStudio.ActualHeight > 100,
            "AppearanceView is laid out with a real size, not an empty white panel");
        ProjectZ.Tests.TextColorTests.Run(Check);
        Console.WriteLine("All WPF appearance smoke tests passed.");
    }

    // The test never calls Application.Run, so queued binding/trigger work would otherwise sit
    // in the dispatcher forever. Drain it down to Background priority like a mini message loop.
    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static T? FindLogicalDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is T match) return match;
            if (child is DependencyObject node)
            {
                var result = FindLogicalDescendant<T>(node);
                if (result is not null) return result;
            }
        }
        return null;
    }
}
