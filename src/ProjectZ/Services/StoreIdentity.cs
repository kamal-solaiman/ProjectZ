using System.ComponentModel;
using ProjectZ.Models;

namespace ProjectZ.Services;

/// <summary>
/// The live store identity shown by every window, the side menu, the login screen and the print previews.
/// One singleton (<see cref="Current"/>) is registered in <c>App.Resources["StoreIdentity"]</c>, so XAML binds to it
/// with <c>Source={StaticResource StoreIdentity}</c> and updates automatically when the settings screen saves a new
/// store name — no restart and no window rebuild.
/// </summary>
public sealed class StoreIdentity : INotifyPropertyChanged
{
    // Every instance created from XAML subscribes here, so the resource instance and Current stay in sync.
    private static readonly List<StoreIdentity> Instances = [];
    private static StoreProfile profile = StoreProfile.Defaults;

    public static StoreIdentity Current { get; } = Create();

    /// <summary>Public because the resource dictionary instantiates it; every instance is kept in sync.</summary>
    public StoreIdentity()
    {
        Instances.Add(this);
    }

    private static StoreIdentity Create() => new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after <see cref="Apply"/> so screens can refresh computed labels.</summary>
    public static event Action? Changed;

    /// <summary>Raised when a screen that subscribed to <see cref="Changed"/> is torn down.</summary>
    public event Action? Disconnected;

    // ------------------------------------------------------------------ live values

    public StoreProfile Profile => profile;

    /// <summary>اسم المحل / اسم البرنامج. يظهر في كل القوائم والنوافذ وشاشة الدخول والمطبوعات.</summary>
    public string Name => profile.StoreName;
    public string Tagline => profile.Tagline;
    public string Address => profile.Address;
    public string Phone => profile.Phone;
    public string PhoneAlt => profile.PhoneAlt;
    public string Email => profile.Email;
    public string ServiceCenterName => profile.ServiceCenterName;
    public string FooterNote => profile.FooterNote;
    public string CurrencyLabel => profile.CurrencyLabel;
    public string CurrencyCode => profile.CurrencyCode;
    public int DefaultLowStockAlert => profile.DefaultLowStockAlert;

    /// <summary>Headline printed on receipts: store name followed by its tagline.</summary>
    public string Headline => profile.Headline;

    /// <summary>Address and phones printed under the store name.</summary>
    public string ContactLine => profile.ContactLine;
    public bool HasContactLine => profile.HasContactLine;

    /// <summary>«NewPhones — مركز الصيانة والخدمة» header used on maintenance receipts.</summary>
    public string ServiceCenterHeader => string.IsNullOrWhiteSpace(profile.ServiceCenterName)
        ? profile.StoreName
        : $"{profile.StoreName} — {profile.ServiceCenterName}";

    /// <summary>Main window title, e.g. «NewPhones».</summary>
    public string WindowTitle => profile.StoreName;

    /// <summary>Applies a saved profile and notifies every window bound to this object.</summary>
    public static void Apply(StoreProfile saved)
    {
        profile = saved;

        // The currency label is used by the shared amount formatter (PosFormatting).
        PosFormatting.CurrencyLabel = saved.CurrencyLabel;

        foreach (var instance in Instances.ToList())
            instance.PropertyChanged?.Invoke(instance, new PropertyChangedEventArgs(null));

        Changed?.Invoke();
    }

    /// <summary>Tells every listener that the screen owning the subscription is closing.</summary>
    public static void NotifyClosing() => Current.Disconnected?.Invoke();

    /// <summary>
    /// Removes a long-lived screen from <see cref="Changed"/> so a closed session is collectable.
    /// This must not raise <see cref="Disconnected"/>: the window that owns the handler calls this
    /// from inside that event, and re-raising it would recurse until the process crashes.
    /// </summary>
    public static void Disconnect(Action? handler)
    {
        if (handler is null) return;
        Changed -= handler;
    }
}
