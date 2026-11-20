using System.Collections.ObjectModel;
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
        // «المصروفات» sits under «التقارير» and above «المستخدمين». Only implemented
        // items get a live screen; the other report items must retain the placeholder.
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
        PumpDispatcher();
        var hostedReport = FindLogicalDescendant<SalesReportsView>(main);
        Check(mainVm.IsImplementedScreenSelected && mainVm.IsSalesReportsSelected && mainVm.SelectedPageTitle == "تقارير المبيعات"
            && hostedReport?.DataContext == mainVm.SalesReports && ((Grid)hostedReport.Parent).Visibility == Visibility.Visible,
            "Sales reports have a real, visible host; offline preview does not invent balances");
        mainVm.SelectItem(reportsMenu.Items[1]);
        Check(!mainVm.IsImplementedScreenSelected, "Unimplemented report categories still show the placeholder");
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
        // Regression: «حساباتي» kept showing a frozen «النقدية» balance because every reload replaced
        // the account rows while the cash card stayed bound to the old instance. An account snapshot
        // must now update the same objects, so their BalanceDisplay notification reaches the card.
        var liveAccounts = FinanceDesignSeedData.CreateAccounts();
        var liveCash = liveAccounts.First(account => account.IsCash);
        var cashBefore = liveCash.Balance;
        var cashCardRefreshes = 0;
        liveCash.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(FinancialAccountDesignItem.BalanceDisplay)) cashCardRefreshes++;
        };
        var snapshotAccounts = FinanceDesignSeedData.CreateAccounts().ToList();
        snapshotAccounts.First(account => account.IsCash).Balance = cashBefore - 125.5m;
        snapshotAccounts.RemoveAt(snapshotAccounts.Count - 1); // a wallet deleted in SQL Server
        FinancialAccountSnapshot.Apply(liveAccounts, snapshotAccounts);
        Check(ReferenceEquals(liveCash, liveAccounts.First(account => account.IsCash))
              && liveCash.Balance == cashBefore - 125.5m,
            "Applying an account snapshot updates the same cash object instead of replacing it");
        Check(cashCardRefreshes > 0,
            "The cash row raises BalanceDisplay so the cash card re-reads the new amount");
        Check(liveAccounts.Count == snapshotAccounts.Count,
            "Applying a snapshot removes accounts that no longer exist in the database");

        var financeCategory = mainVm.Categories.Single(category => category.Title == "المالية");
        var myAccountsItem = financeCategory.Items.Single(item => item.Title == "حساباتي");
        mainVm.SelectItem(myAccountsItem);
        PumpDispatcher();
        Check(mainVm.IsMyAccountsSelected, "SelectItem activates the accounts screen");
        Check(ReferenceEquals(mainVm.MyAccounts.CashAccount, mainVm.MyAccounts.Accounts.First(a => a.IsCash))
              && mainVm.MyAccounts.HasCashAccount,
            "The accounts screen exposes the cash row the cash card binds to");
        var hostedAccounts = FindLogicalDescendant<MyAccountsView>(main);
        Check(hostedAccounts is not null && hostedAccounts.DataContext is MyAccountsViewModel,
            "MainWindow hosts MyAccountsView with the accounts view model");
        Check(((Grid)hostedAccounts!.Parent).Visibility == Visibility.Visible,
            "Accounts host Grid is Visible after navigating to حساباتي");
        // Regression: pressing «تعديل» on a Vodafone Cash operation whose line was stopped or deleted
        // from «حساباتي» crashed with System.InvalidOperationException "Sequence contains no matching
        // element" (Enumerable.ThrowNoMatchException), because ApplyEdit/CanUndo resolved the wallet with
        // accounts.Accounts.First(w => w.Id == operation.WalletId). The ledger keeps the operation after
        // its line disappears, since LoadAccountsInto only loads IsActive = 1 rows.
        var guardAccounts = new MyAccountsViewModel();
        var guardTransfers = new VodafoneTransferViewModel(guardAccounts, "اختبار الحماية");
        var guardRecords = new VodafoneRecordsViewModel(guardTransfers, guardAccounts, "اختبار الحماية");
        var orphanedOperation = guardRecords.FilteredOperations.First(operation => operation.Id == 1);
        var originalWalletId = orphanedOperation.WalletId;
        orphanedOperation.WalletId = 999; // the saved line is no longer one of the active accounts
        var editorOpened = false;
        guardRecords.EditRequested = () => editorOpened = true;
        guardRecords.BeginOperationEditCommand.Execute(orphanedOperation);
        Check(!editorOpened && guardRecords.IsErrorStatus
              && guardRecords.StatusMessage == VodafoneRecordsViewModel.MissingWalletMessage,
            "Editing an operation whose line is gone explains it in Arabic instead of throwing");
        // The dialog can already be open when the line disappears, so the save path must be guarded too.
        orphanedOperation.WalletId = originalWalletId;
        guardRecords.BeginOperationEditCommand.Execute(orphanedOperation);
        Check(editorOpened, "A valid line opens the editor");
        guardAccounts.Accounts.Remove(guardRecords.EditWallet!);
        guardRecords.EditWallet = guardAccounts.Accounts.First(account => account.Kind == FinancialAccountKind.VodafoneCash);
        guardRecords.EditKind = "إيداع للعميل";
        guardRecords.EditCustomerNumber = "01012345678";
        guardRecords.EditAmountText = "100";
        guardRecords.EditCommissionText = "1";
        var cashBeforeOrphanEdit = guardAccounts.CashAccount!.Balance;
        Check(!guardRecords.ApplyEdit()
              && guardRecords.StatusMessage == VodafoneRecordsViewModel.MissingWalletMessage,
            "Saving an edit for an operation whose line is gone reports it instead of crashing");
        Check(guardAccounts.CashAccount!.Balance == cashBeforeOrphanEdit,
            "A refused edit leaves the cash balance exactly as it was");
        guardRecords.ReverseOperationCommand.Execute(orphanedOperation);
        Check(guardRecords.IsErrorStatus && !orphanedOperation.IsReversed,
            "Reversing an operation whose line is gone reports it instead of crashing");
        guardRecords.DeleteOperationCommand.Execute(orphanedOperation);
        Check(guardRecords.FilteredOperations.Contains(orphanedOperation),
            "Deleting an operation whose line is gone refuses instead of dropping the record");
        var selectionAccounts = new MyAccountsViewModel();
        var selectionRecords = new VodafoneRecordsViewModel(new VodafoneTransferViewModel(selectionAccounts, "اختبار"), selectionAccounts, "اختبار");
        var opened = selectionRecords.FilteredOperations.First(o => o.Id == 1 && o.IsCustomerTransfer);
        var unrelated = selectionRecords.FilteredOperations.First(o => o.Id == 2 && o.IsCustomerTransfer);
        var untouched = unrelated.Amount;
        selectionRecords.BeginOperationEditCommand.Execute(opened);
        selectionRecords.SelectedOperation = unrelated;
        selectionRecords.EditAmountText = "10";
        selectionRecords.EditKind = "سحب للعميل";
        selectionRecords.EditCommissionText = "1";
        Check(selectionRecords.ApplyEdit() && opened.IsReversed && opened.Amount == 1500 && unrelated.Amount == untouched,
            "Changing the list selection cannot retarget an already-open transfer editor");
        var replacement = selectionRecords.FilteredOperations.Single(o => o.CorrectsTransferId == opened.Id);
        Check(replacement.Amount == 10 && !replacement.IsReversed, "The corrected row is linked to the original");
        Action? confirmDelete = null;
        selectionRecords.ConfirmRequested = (_, action) => confirmDelete = action;
        selectionRecords.DeleteOperationCommand.Execute(replacement);
        selectionRecords.SelectedOperation = unrelated;
        confirmDelete!();
        Check(replacement.IsReversed && !unrelated.IsReversed && selectionRecords.FilteredOperations.Contains(replacement),
            "Delete confirmation reverses the confirmed transfer and retains its history");
        var instapayGuardAccounts = new MyAccountsViewModel();
        var instapayGuardRecords = new InstapayRecordsViewModel(
            new InstapayTransferViewModel(instapayGuardAccounts, "اختبار الحماية"), instapayGuardAccounts, "اختبار الحماية");
        var orphanedInstapay = instapayGuardRecords.FilteredOperations.First(operation => operation.Id == 1);
        orphanedInstapay.WalletId = 999;
        var instapayEditorOpened = false;
        instapayGuardRecords.EditRequested = () => instapayEditorOpened = true;
        instapayGuardRecords.BeginOperationEditCommand.Execute(orphanedInstapay);
        Check(!instapayEditorOpened && instapayGuardRecords.IsErrorStatus
              && instapayGuardRecords.StatusMessage == InstapayRecordsViewModel.MissingWalletMessage,
            "The InstaPay screen reports a removed wallet in Arabic instead of throwing");
        // «النقدية» is optional after the nullable cash-account fix: an edit or a transfer without an
        // active cash row must explain itself instead of throwing a NullReferenceException.
        var noCashAccounts = new MyAccountsViewModel();
        var noCashRecords = new VodafoneRecordsViewModel(
            new VodafoneTransferViewModel(noCashAccounts, "اختبار الحماية"), noCashAccounts, "اختبار الحماية");
        noCashAccounts.Accounts.Remove(noCashAccounts.CashAccount!);
        var cashlessOperation = noCashRecords.FilteredOperations.First(operation => operation.Id == 1);
        noCashRecords.BeginOperationEditCommand.Execute(cashlessOperation);
        Check(noCashRecords.EditWallet is not null,
            "The editor still opens while the operation's own line is active");
        noCashRecords.EditKind = "إيداع للعميل";
        noCashRecords.EditCustomerNumber = "01012345678";
        noCashRecords.EditAmountText = "100";
        noCashRecords.EditCommissionText = "1";
        Check(!noCashRecords.ApplyEdit()
              && noCashRecords.StatusMessage == VodafoneRecordsViewModel.MissingCashMessage,
            "Saving an edit without an active cash account reports it instead of throwing");
        var cashlessTransfer = new VodafoneTransferViewModel(noCashAccounts, "اختبار الحماية");
        cashlessTransfer.SelectWalletCommand.Execute(
            noCashAccounts.Accounts.First(account => account.Kind == FinancialAccountKind.VodafoneCash && account.IsActive));
        cashlessTransfer.CustomerNumber = "01012345678";
        cashlessTransfer.AmountText = "100";
        cashlessTransfer.CommissionText = "2";
        Check(cashlessTransfer.CashBalanceAfterPreview == "—",
            "The cash preview shows a dash, not a crash, when the cash account is missing");
        cashlessTransfer.ExecuteOperationCommand.Execute(null);
        Check(cashlessTransfer.IsErrorStatus
              && cashlessTransfer.StatusMessage == VodafoneTransferViewModel.MissingCashMessage,
            "Executing a transfer without an active cash account reports it in Arabic");
        // «تعيين افتراضية» used to reorder the local list only, so the choice vanished on the next
        // reload or restart. The design-mode path must still flag exactly one default per provider and
        // must never touch another provider's default.
        var defaultAccounts = new MyAccountsViewModel();
        var secondVodafone = defaultAccounts.Accounts.First(account =>
            account.Kind == FinancialAccountKind.VodafoneCash && !account.IsDefault);
        defaultAccounts.SelectedAccount = secondVodafone;
        defaultAccounts.SetSelectedAsDefaultCommand.Execute(null);
        Check(secondVodafone.IsDefault && !defaultAccounts.IsErrorStatus,
            "«تعيين افتراضية» flags the chosen wallet");
        Check(defaultAccounts.Accounts.Count(account =>
                  account.Kind == FinancialAccountKind.VodafoneCash && account.IsDefault) == 1,
            "Only one line stays default per provider");
        Check(defaultAccounts.Accounts.First(account =>
                  account.Kind == FinancialAccountKind.InstaPay && account.IsDefault).IsDefault,
            "Making a Vodafone line default keeps the InstaPay default");
        var stoppedWallet = defaultAccounts.Accounts.First(account => !account.IsCash && !account.IsActive);
        defaultAccounts.SelectedAccount = stoppedWallet;
        defaultAccounts.SetSelectedAsDefaultCommand.Execute(null);
        Check(defaultAccounts.IsErrorStatus && !stoppedWallet.IsDefault
              && defaultAccounts.StatusMessage.Contains("فعّل المحفظة أولًا", StringComparison.Ordinal),
            "A stopped wallet is refused as default with an Arabic message");
        var toggleWallet = defaultAccounts.Accounts.First(account =>
            account.Kind == FinancialAccountKind.InstaPay && account.IsActive);
        defaultAccounts.SelectedAccount = toggleWallet;
        defaultAccounts.ToggleSelectedAccountCommand.Execute(null);
        Check(!toggleWallet.IsActive, "«تفعيل / إيقاف» stops the wallet");
        defaultAccounts.ToggleSelectedAccountCommand.Execute(null);
        Check(toggleWallet.IsActive, "The same button reactivates it");
        defaultAccounts.SelectedAccount = defaultAccounts.CashAccount!;
        defaultAccounts.ToggleSelectedAccountCommand.Execute(null);
        Check(defaultAccounts.IsErrorStatus && defaultAccounts.CashAccount!.IsActive,
            "The cash account cannot be stopped from the toggle");
        // «سجلات فودافون كاش» و«سجلات إنستاباي» showed only dbo.CustomerTransfers, so every sale,
        // purchase, expense, maintenance payment or wallet transfer was missing from them. Each screen
        // now merges the whole wallet history and keeps another screen's movement read-only.
        var historyAccounts = new MyAccountsViewModel();
        var historyRecords = new VodafoneRecordsViewModel(
            new VodafoneTransferViewModel(historyAccounts, "اختبار السجلات"), historyAccounts, "اختبار السجلات");
        Check(historyRecords.FilteredOperations.Any(operation => operation.IsCustomerTransfer)
              && historyRecords.FilteredOperations.Any(operation => !operation.IsCustomerTransfer),
            "The Vodafone records screen lists wallet movements beside customer transfers");
        Check(historyRecords.KindFilters.Contains("إيداع للعميل")
              && historyRecords.KindFilters.Any(title => title != "كل العمليات" && title != "إيداع للعميل" && title != "سحب للعميل"),
            "The kind filter offers the new wallet sources, not only deposits and withdrawals");
        var otherScreenMovement = historyRecords.FilteredOperations.First(operation => !operation.IsCustomerTransfer);
        Check(!otherScreenMovement.CanEditOrDelete && otherScreenMovement.StatusTitle == "ناجحة",
            "A movement that belongs to another screen cannot be edited, reversed or deleted here");
        historyRecords.BeginOperationEditCommand.Execute(otherScreenMovement);
        Check(historyRecords.StatusMessage == VodafoneRecordsViewModel.ReadOnlyMovementMessage,
            "Editing another screen's movement explains it in Arabic");
        historyRecords.ReverseOperationCommand.Execute(otherScreenMovement);
        Check(historyRecords.StatusMessage == VodafoneRecordsViewModel.ReadOnlyMovementMessage,
            "Reversing another screen's movement explains it in Arabic");
        historyRecords.DeleteOperationCommand.Execute(otherScreenMovement);
        Check(historyRecords.StatusMessage == VodafoneRecordsViewModel.ReadOnlyMovementMessage
              && historyRecords.FilteredOperations.Contains(otherScreenMovement),
            "Deleting another screen's movement refuses instead of dropping the record");
        var instapayHistoryAccounts = new MyAccountsViewModel();
        var instapayHistoryRecords = new InstapayRecordsViewModel(
            new InstapayTransferViewModel(instapayHistoryAccounts, "اختبار السجلات"), instapayHistoryAccounts, "اختبار السجلات");
        Check(instapayHistoryRecords.FilteredOperations.Any(operation => !operation.IsCustomerTransfer)
              && instapayHistoryRecords.FilteredOperations.All(operation => operation.WalletId != 0),
            "The InstaPay records screen lists its own wallet movements the same way");
        // «حساباتي» must show the wallets split into فودافون كاش and إنستاباي sections side by side.
        Check(historyAccounts.VodafoneWallets.Count > 0 && historyAccounts.InstapayWallets.Count > 0
              && historyAccounts.VodafoneWallets.All(account => account.Kind == FinancialAccountKind.VodafoneCash)
              && historyAccounts.InstapayWallets.All(account => account.Kind == FinancialAccountKind.InstaPay),
            "«حساباتي» splits the electronic wallets into a Vodafone section and an InstaPay section");
        Check(!historyAccounts.HasNoVodafoneWallets && !historyAccounts.HasNoInstapayWallets
              && historyAccounts.VodafoneWalletsSubtext.Contains("فودافون", StringComparison.Ordinal)
              && historyAccounts.InstapayWalletsSubtext.Contains("إنستاباي", StringComparison.Ordinal),
            "Each wallet section reports its own count and balance in Arabic");
        // «حفظ وإضافة صنف آخر» did the same work as «حفظ وتسجيل الصنف»: the second button and its
        // command are gone, and the remaining button keeps the category and unit for the next product.
        Check(typeof(AddProductViewModel).GetProperty("SaveAndKeepCategoryCommand") is null,
            "The duplicate save command is gone from «إضافة منتج»");
        Check(typeof(AddProductViewModel).GetProperty("SaveProductCommand") is not null,
            "«حفظ وتسجيل الصنف» remains the single save button");
        // The header bar carries Egypt's date and time next to «مرحبًا» and «تسجيل الخروج».
        Check(mainVm.HeaderDate.Length > 0 && mainVm.HeaderTime.Length == 8 && mainVm.HeaderTime[2] == ':',
            "The title bar shows today's date and a running HH:mm:ss clock");
        Check(mainVm.HeaderDate.Contains(DateTime.UtcNow.Year.ToString(), StringComparison.Ordinal)
              || mainVm.HeaderDate.Contains((DateTime.UtcNow.Year + 1).ToString(), StringComparison.Ordinal),
            "The clock date follows Egypt's calendar year, not a placeholder");
        MaintenancePostingTests.Run(Check);
        PurchasePostingTests.Run(Check);
        SalesReportUiTests.Run(Check, PumpDispatcher);
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
