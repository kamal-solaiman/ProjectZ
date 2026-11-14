using System.Globalization;
using System.Threading;
using System.Windows;
using ProjectZ.Data;
using ProjectZ.Models;
using ProjectZ.Services;
using ProjectZ.ViewModels;
using ProjectZ.Views;

namespace ProjectZ;

public partial class App : Application
{
    // Track windows explicitly as MainWindow changes during login and logout.
    private readonly List<Window> liveWindows = new();
    private readonly List<CancellationTokenSource> loginLifetimes = new();
    private AuthenticationService service = null!;
    private readonly ConnectionSettingsProvider connectionSettings = new();
    private bool isShuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        var arabicCulture = CultureInfo.GetCultureInfo("ar-EG");
        CultureInfo.DefaultThreadCurrentCulture = arabicCulture;
        CultureInfo.DefaultThreadCurrentUICulture = arabicCulture;
        Thread.CurrentThread.CurrentCulture = arabicCulture;
        Thread.CurrentThread.CurrentUICulture = arabicCulture;

        base.OnStartup(e);

        // Load the store identity before the first window so the login screen already carries the store
        // name. A missing database or missing grant must never block sign-in, so failures fall back to the
        // ProjectZ defaults and the settings screen reports the real error later.
        try
        {
            StoreIdentity.Apply(new SettingsRepository(connectionSettings).Load());
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Record(ex);
        }

        service = new AuthenticationService(new UserRepository(connectionSettings));
        ShowLogin();
    }

    private void ShowLogin()
    {
        if (isShuttingDown) return;

        var lifetime = new CancellationTokenSource();
        loginLifetimes.Add(lifetime);

        var window = new LoginWindow();
        window.DataContext = new LoginViewModel(
            service,
            () => window.ReadPassword(),
            window.ClearPassword,
            user => OpenMain(window, user),
            lifetime.Token);

        MainWindow = window;
        TrackWindow(window, () => Cancel(lifetime));
        window.Show();
    }

    private void OpenMain(LoginWindow login, UserSession user)
    {
        // A slow SQL login may finish after its window has closed. Do not let
        // that late result recreate a window or keep an invisible process alive.
        if (isShuttingDown || !liveWindows.Contains(login)) return;

        Views.MainWindow main;
        var stage = "تحميل مكونات MainWindow / XAML";
        try
        {
            main = new Views.MainWindow();
            stage = "إنشاء MainViewModel والشاشات المشتركة";
            main.DataContext = new MainViewModel(
                user,
                () => Logout(main),
                new InventoryRepository(connectionSettings, user.Id),
                new SalesRepository(connectionSettings, user.Id),
                new CategoryRepository(connectionSettings, user.Id),
                new PurchaseRepository(connectionSettings, user.Id),
                new FinancialRepository(connectionSettings, user.Id),
                new MaintenanceRepository(connectionSettings, user.Id),
                new UserRepository(connectionSettings, user.Id),
                new SettingsRepository(connectionSettings, user.Id),
                new BackupRepository(connectionSettings));
            MainWindow = main;
            TrackWindow(main);

            // Show the replacement first: closing the old window must not briefly
            // make the application windowless during a successful login.
            stage = "عرض MainWindow وإغلاق نافذة تسجيل الدخول";
            main.Show();
            login.ClearPassword();
            login.Close();
        }
        catch (Exception ex)
        {
            throw new MainWindowInitializationException(stage, ex);
        }
    }

    private void Logout(Window main)
    {
        if (isShuttingDown) return;

        main.DataContext = null;
        ShowLogin();
        main.Close();
    }

    private void TrackWindow(Window window, Action? onClosed = null)
    {
        liveWindows.Add(window);
        window.Closed += (_, _) =>
        {
            liveWindows.Remove(window);
            onClosed?.Invoke();
            window.DataContext = null;

            if (liveWindows.Count == 0)
                ShutdownApplication();
        };
    }

    private void ShutdownApplication()
    {
        if (isShuttingDown) return;

        isShuttingDown = true;
        CancelPendingLogins();
        // OnLastWindowClose also requests shutdown; this explicit call is a
        // guarded fallback so no WPF dispatcher is left running without a UI.
        Shutdown();
    }

    private static void Cancel(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The source was already cleaned up during application exit.
        }
    }

    private void CancelPendingLogins()
    {
        foreach (var lifetime in loginLifetimes)
            Cancel(lifetime);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        isShuttingDown = true;
        CancelPendingLogins();
        foreach (var lifetime in loginLifetimes)
            lifetime.Dispose();

        loginLifetimes.Clear();
        liveWindows.Clear();
        base.OnExit(e);
    }
}
