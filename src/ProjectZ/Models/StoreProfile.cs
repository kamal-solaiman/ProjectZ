namespace ProjectZ.Models;

/// <summary>
/// The store identity and general defaults saved in dbo.SystemSettings.
/// The store name doubles as the program name: changing it renames the application across every window,
/// the side menu, the login screen and the printed documents without rebuilding or restarting.
/// </summary>
public sealed record StoreProfile
{
    public const string DefaultStoreName = "ProjectZ";
    public const string DefaultTagline = "للموبايل والإكسسوارات";
    public const string DefaultServiceCenter = "مركز الصيانة والخدمة";
    public const string DefaultAddress = "";
    public const string DefaultPhone = "";
    public const string DefaultPhoneAlt = "";
    public const string DefaultEmail = "";
    public const string DefaultFooterNote = "شكرًا لتعاملكم معنا · يُرجى الاحتفاظ بالفاتورة للضمان والاستبدال";
    public const string DefaultCurrencyLabel = "ج.م";
    public const string DefaultCurrencyCode = "EGP";
    public const int DefaultLowStockAlertValue = 5;

    public string StoreName { get; init; } = DefaultStoreName;
    public string Tagline { get; init; } = DefaultTagline;
    public string Address { get; init; } = DefaultAddress;
    public string Phone { get; init; } = DefaultPhone;
    public string PhoneAlt { get; init; } = DefaultPhoneAlt;
    public string Email { get; init; } = DefaultEmail;
    public string ServiceCenterName { get; init; } = DefaultServiceCenter;
    public string FooterNote { get; init; } = DefaultFooterNote;
    public string CurrencyLabel { get; init; } = DefaultCurrencyLabel;
    public string CurrencyCode { get; init; } = DefaultCurrencyCode;
    public int DefaultLowStockAlert { get; init; } = DefaultLowStockAlertValue;

    public static StoreProfile Defaults => new();

    /// <summary>«اسم المحل للموبايل والإكسسوارات» — the headline used on receipts.</summary>
    public string Headline => string.IsNullOrWhiteSpace(Tagline) ? StoreName : $"{StoreName} {Tagline}";

    /// <summary>Address followed by the phones, shown under the store name on printed documents.</summary>
    public string ContactLine
    {
        get
        {
            var parts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(Address)) parts.Add(Address.Trim());
            var phones = new[] { Phone, PhoneAlt }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct()
                .ToList();
            if (phones.Count > 0) parts.Add("هاتف: " + string.Join(" / ", phones));
            if (!string.IsNullOrWhiteSpace(Email)) parts.Add(Email.Trim());
            return string.Join(" · ", parts);
        }
    }

    public bool HasContactLine => !string.IsNullOrWhiteSpace(ContactLine);
}
