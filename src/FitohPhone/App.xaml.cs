using System.Windows;
using FitohPhone.Data;
using FitohPhone.Models;
using FitohPhone.Services;
using FitohPhone.ViewModels;
using FitohPhone.Views;

namespace FitohPhone;

public partial class App : Application
{
    // Every window the app opens is tracked here. Shutting down is driven by this
    // list rather than by MainWindow identity, because MainWindow is reassigned
    // on every login/logout and a stale reference can suppress the exit call.
    private readonly List<Window> liveWindows = new();
    private readonly List<CancellationTokenSource> loginLifetimes = new();
    private UserSession? session;
    private ConnectionSettingsProvider settings = null!;
    private AuthenticationService service = null!;
    private bool isShuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        settings = new ConnectionSettingsProvider();
        service = new AuthenticationService(new UserRepository(settings));
        ShowLogin();
    }

    private void ShowLogin()
    {
        if (isShuttingDown) return;

        var lifetime = new CancellationTokenSource();
        loginLifetimes.Add(lifetime);
        var window = new LoginWindow();
        window.DataContext = new LoginViewModel(service, () => window.ReadPassword(), window.ClearPassword,
            user => OpenMain(window, user), lifetime.Token, settings);
        MainWindow = window;
        TrackWindow(window, () => Cancel(lifetime));
        window.Show();
    }

    private void OpenMain(LoginWindow login, UserSession user)
    {
        // A SQL sign-in can finish after the user closed the login window (slow
        // LAN, acceptance test 13). Never open a window for a dead login or
        // after shutdown started: that is what leaves an invisible process.
        if (isShuttingDown || !liveWindows.Contains(login)) return;

        session = user;
        var main = new Views.MainWindow();
        main.DataContext = new MainViewModel(user, () => Logout(main));
        MainWindow = main;
        TrackWindow(main);
        // Show the replacement BEFORE closing the old window so the window count
        // never drops to zero during a swap (ShutdownMode=OnLastWindowClose).
        main.Show();
        login.ClearPassword();
        login.Close();
    }

    private void Logout(Window main)
    {
        if (isShuttingDown) return;
        session = null;
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
            if (ReferenceEquals(MainWindow, window)) session = null;
            if (liveWindows.Count == 0) ShutdownApplication();
        };
    }

    private void ShutdownApplication()
    {
        // Exactly once. Application.Shutdown() itself ignores repeated calls,
        // but the extra guard also stops any late navigation from this app.
        if (isShuttingDown) return;
        isShuttingDown = true;
        session = null;
        CancelPendingLogins();
        // OnLastWindowClose already requests shutdown; this explicit call is the
        // safety net for hidden/orphan windows still registered with WPF.
        Shutdown();
    }

    private static void Cancel(CancellationTokenSource source)
    {
        try { source.Cancel(); }
        catch (ObjectDisposedException) { /* already cleaned up at exit */ }
    }

    private void CancelPendingLogins()
    {
        foreach (var lifetime in loginLifetimes) Cancel(lifetime);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        isShuttingDown = true;
        session = null;
        // Cancel before disposing: an awaited SqlConnection.OpenAsync may still
        // hold a registration on the token. Disposing it in Window.Closed used
        // to race with that registration.
        CancelPendingLogins();
        foreach (var lifetime in loginLifetimes) lifetime.Dispose();
        loginLifetimes.Clear();
        liveWindows.Clear();
        base.OnExit(e);
    }
}
