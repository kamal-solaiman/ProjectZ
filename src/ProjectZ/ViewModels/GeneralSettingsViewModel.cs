using System.Globalization;
using ProjectZ.Data;
using ProjectZ.Helpers;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.ViewModels;

/// <summary>
/// The general settings screen: store identity (which is also the program name) and the general defaults
/// stored in dbo.SystemSettings. Themes and backup are separate screens and are not handled here.
/// </summary>
public sealed class GeneralSettingsViewModel : ObservableObject
{
    private readonly SettingsRepository? repository;

    private StoreProfile loaded = StoreProfile.Defaults;

    private string storeName = "";
    private string tagline = "";
    private string address = "";
    private string phone = "";
    private string phoneAlt = "";
    private string email = "";
    private string serviceCenterName = "";
    private string footerNote = "";
    private string currencyLabel = "";
    private string currencyCode = "";
    private string lowStockAlertText = "5";

    private string statusMessage = "";
    private bool isErrorStatus;
    private bool isDirty;

    public GeneralSettingsViewModel(string cashierName = "المشرف", SettingsRepository? repository = null)
    {
        CashierName = cashierName;
        this.repository = repository;

        SaveCommand = new ActionCommand(Save);
        ReloadCommand = new ActionCommand(() => RefreshFromDatabase());
        ResetDefaultsCommand = new ActionCommand(ResetToDefaults);

        if (repository is null)
        {
            const string note = "وضع العرض التجريبي: لا يوجد اتصال بقاعدة البيانات، ولن تُحفظ أي إعدادات.";
            LoadInto(StoreProfile.Defaults);
            SetStatus(note, false);
        }
        else
        {
            SetStatus("تُقرأ الإعدادات من جدول dbo.SystemSettings، وأي عرض يعتمد على اسم المحل يتحدث فور الحفظ. المظهر والنسخ الاحتياطي في شاشتيهما المستقلتين، ولا تُدار من هنا.", false);
        }
    }

    public string CashierName { get; }
    public bool IsDatabaseBacked => repository is not null;
    public string ConnectionBadgeText => IsDatabaseBacked ? "متصل بقاعدة البيانات" : "عرض تصميم بدون قاعدة بيانات";

    // ------------------------------------------------------------------ fields

    /// <summary>اسم المحل: هو نفسه اسم البرنامج في كل القوائم والنوافذ وشاشة الدخول والمطبوعات.</summary>
    public string StoreName
    {
        get => storeName;
        set => SetField(ref storeName, value, nameof(StoreName), nameof(PreviewTitle), nameof(PreviewHeadline), nameof(StoreNameHint));
    }

    public string Tagline
    {
        get => tagline;
        set => SetField(ref tagline, value, nameof(Tagline), nameof(PreviewHeadline));
    }

    public string Address
    {
        get => address;
        set => SetField(ref address, value, nameof(Address), nameof(PreviewContactLine), nameof(HasPreviewContactLine));
    }

    public string Phone
    {
        get => phone;
        set => SetField(ref phone, value, nameof(Phone), nameof(PreviewContactLine), nameof(HasPreviewContactLine));
    }

    public string PhoneAlt
    {
        get => phoneAlt;
        set => SetField(ref phoneAlt, value, nameof(PhoneAlt), nameof(PreviewContactLine), nameof(HasPreviewContactLine));
    }

    public string Email
    {
        get => email;
        set => SetField(ref email, value, nameof(Email), nameof(PreviewContactLine), nameof(HasPreviewContactLine));
    }

    public string ServiceCenterName
    {
        get => serviceCenterName;
        set => SetField(ref serviceCenterName, value, nameof(ServiceCenterName), nameof(PreviewServiceCenter));
    }

    public string FooterNote
    {
        get => footerNote;
        set => SetField(ref footerNote, value, nameof(FooterNote), nameof(PreviewFooter));
    }

    public string CurrencyLabel
    {
        get => currencyLabel;
        set => SetField(ref currencyLabel, value, nameof(CurrencyLabel), nameof(PreviewAmount));
    }

    public string CurrencyCode
    {
        get => currencyCode;
        set => SetField(ref currencyCode, value, nameof(CurrencyCode));
    }

    public string LowStockAlertText
    {
        get => lowStockAlertText;
        set => SetField(ref lowStockAlertText, value, nameof(LowStockAlertText));
    }

    // ------------------------------------------------------------------ preview

    /// <summary>Card header: the store name exactly as it will appear in the side menu and window titles.</summary>
    public string PreviewTitle => string.IsNullOrWhiteSpace(StoreName) ? "—" : StoreName.Trim();

    public string PreviewHeadline => BuildProfile().Headline;
    public string PreviewContactLine => BuildProfile().ContactLine;
    public bool HasPreviewContactLine => BuildProfile().HasContactLine;
    public string PreviewServiceCenter => string.IsNullOrWhiteSpace(ServiceCenterName) ? "—" : ServiceCenterName.Trim();
    public string PreviewFooter => string.IsNullOrWhiteSpace(FooterNote) ? "—" : FooterNote.Trim();
    public string PreviewAmount => $"1,250.00 {(string.IsNullOrWhiteSpace(CurrencyLabel) ? StoreProfile.DefaultCurrencyLabel : CurrencyLabel.Trim())}";

    /// <summary>Visible hint so an unsaved rename is never left behind by switching screens.</summary>
    public string UnsavedHint => "توجد تغييرات لم تُحفظ بعد. اضغط «حفظ الإعدادات» لتثبيتها في قاعدة البيانات.";

    public string StoreNameHint =>
        $"سيظهر «{PreviewTitle}» في: عنوان النافذة الرئيسية، شريط القائمة الجانبية، شاشة تسجيل الدخول، عناوين نوافذ الإضافة والتعديل، رؤوس المطبوعات (الفواتير وإيصالات الصيانة والشراء)، وعبارة الترحيب.";

    // ------------------------------------------------------------------ status

    /// <summary>True when a field differs from what is stored, so closing can warn about unsaved edits.</summary>
    public bool IsDirty
    {
        get => isDirty;
        private set
        {
            if (isDirty == value) return;
            isDirty = value;
            Changed();
            Changed(nameof(HasUnsavedChanges));
            if (value) StatusMessage = UnsavedHint;
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
    public bool HasUnsavedChanges => IsDirty;

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

    public ActionCommand SaveCommand { get; }
    public ActionCommand ReloadCommand { get; }
    public ActionCommand ResetDefaultsCommand { get; }

    /// <summary>Raised after a successful SQL save so windows that are already open can refresh their titles.</summary>
    public Action? SettingsSaved { get; set; }

    // ------------------------------------------------------------------ load / save

    /// <summary>Reloads the settings sheet from dbo.SystemSettings.</summary>
    public bool RefreshFromDatabase()
    {
        if (repository is null)
        {
            LoadInto(StoreProfile.Defaults);
            SetStatus("وضع العرض التجريبي: لا يمكن القراءة من قاعدة البيانات.", true);
            return false;
        }

        try
        {
            var profile = repository.Load();
            LoadInto(profile);
            StoreIdentity.Apply(profile);
        }
        catch (Exception ex)
        {
            SetStatus(DescribeError(ex, "تعذر تحميل الإعدادات من قاعدة البيانات."), true);
            return false;
        }

        SetStatus("تم تحميل الإعدادات من جدول dbo.SystemSettings.", false);
        return true;
    }

    private void Save()
    {
        if (repository is null)
        {
            SetStatus("وضع العرض التجريبي: لا يمكن الحفظ بدون قاعدة بيانات.", true);
            return;
        }

        if (!TryBuildProfile(out var profile, out var error))
        {
            SetStatus(error, true);
            return;
        }

        var renamed = !string.Equals(profile.StoreName, loaded.StoreName, StringComparison.Ordinal);

        StoreProfile saved;
        try
        {
            saved = repository.Save(profile);
        }
        catch (Exception ex)
        {
            SetStatus(DescribeError(ex, "تعذر حفظ الإعدادات في قاعدة البيانات."), true);
            return;
        }

        LoadInto(saved);
        // Applies to every open window, the sidebar, the login screen and the print previews at once.
        StoreIdentity.Apply(saved);
        SettingsSaved?.Invoke();

        SetStatus(renamed
            ? $"تم حفظ الإعدادات. اسم البرنامج أصبح «{saved.StoreName}» وتم تحديث النوافذ والقوائم وشاشة الدخول والمطبوعات فورًا."
            : "تم حفظ الإعدادات العامة في قاعدة البيانات.", false);
    }

    private void ResetToDefaults()
    {
        LoadInto(StoreProfile.Defaults);
        SetStatus("تمت إعادة الحقول إلى القيم الافتراضية (ProjectZ). اضغط «حفظ الإعدادات» لتثبيتها في قاعدة البيانات.", false);
    }

    private void LoadInto(StoreProfile profile)
    {
        loaded = profile;
        storeName = profile.StoreName;
        tagline = profile.Tagline;
        address = profile.Address;
        phone = profile.Phone;
        phoneAlt = profile.PhoneAlt;
        email = profile.Email;
        serviceCenterName = profile.ServiceCenterName;
        footerNote = profile.FooterNote;
        currencyLabel = profile.CurrencyLabel;
        currencyCode = profile.CurrencyCode;
        lowStockAlertText = profile.DefaultLowStockAlert.ToString(CultureInfo.InvariantCulture);
        IsDirty = false;
        SetStatus("", false);

        Changed(string.Empty);
        Changed(nameof(StoreName));
        Changed(nameof(Tagline));
        Changed(nameof(Address));
        Changed(nameof(Phone));
        Changed(nameof(PhoneAlt));
        Changed(nameof(Email));
        Changed(nameof(ServiceCenterName));
        Changed(nameof(FooterNote));
        Changed(nameof(CurrencyLabel));
        Changed(nameof(CurrencyCode));
        Changed(nameof(LowStockAlertText));
        Changed(nameof(PreviewTitle));
        Changed(nameof(PreviewHeadline));
        Changed(nameof(PreviewContactLine));
        Changed(nameof(HasPreviewContactLine));
        Changed(nameof(PreviewServiceCenter));
        Changed(nameof(PreviewFooter));
        Changed(nameof(PreviewAmount));
        Changed(nameof(StoreNameHint));
    }

    private StoreProfile BuildProfile() => new()
    {
        StoreName = string.IsNullOrWhiteSpace(storeName) ? StoreProfile.DefaultStoreName : storeName.Trim(),
        Tagline = tagline,
        Address = address,
        Phone = phone,
        PhoneAlt = phoneAlt,
        Email = email,
        ServiceCenterName = serviceCenterName,
        FooterNote = footerNote,
        CurrencyLabel = string.IsNullOrWhiteSpace(currencyLabel) ? StoreProfile.DefaultCurrencyLabel : currencyLabel,
        CurrencyCode = currencyCode,
        DefaultLowStockAlert = loaded.DefaultLowStockAlert
    };

    private bool TryBuildProfile(out StoreProfile profile, out string error)
    {
        profile = StoreProfile.Defaults;
        error = "";

        if (string.IsNullOrWhiteSpace(StoreName))
        {
            error = "يرجى إدخال اسم المحل؛ فهو اسم البرنامج الظاهر في كل الشاشات.";
            return false;
        }

        if (StoreName.Trim().Length > 60)
        {
            error = "اسم المحل يجب ألا يتجاوز 60 حرفًا.";
            return false;
        }

        if (!int.TryParse(LowStockAlertText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lowStock)
            || lowStock < 0 || lowStock > 100_000)
        {
            error = "حد التنبيه الافتراضي للمخزون يجب أن يكون رقمًا صحيحًا بين 0 و100000.";
            return false;
        }

        profile = new StoreProfile
        {
            StoreName = StoreName.Trim(),
            Tagline = Tagline.Trim(),
            Address = Address.Trim(),
            Phone = Phone.Trim(),
            PhoneAlt = PhoneAlt.Trim(),
            Email = Email.Trim(),
            ServiceCenterName = ServiceCenterName.Trim(),
            FooterNote = FooterNote.Trim(),
            CurrencyLabel = string.IsNullOrWhiteSpace(CurrencyLabel) ? StoreProfile.DefaultCurrencyLabel : CurrencyLabel.Trim(),
            CurrencyCode = string.IsNullOrWhiteSpace(CurrencyCode) ? StoreProfile.DefaultCurrencyCode : CurrencyCode.Trim(),
            DefaultLowStockAlert = lowStock
        };
        return true;
    }

    // ------------------------------------------------------------------ helpers

    private void SetField(ref string field, string? value, string propertyName, params string[] dependents)
    {
        var clean = value ?? "";
        if (field == clean) return;

        field = clean;
        Changed(propertyName);
        foreach (var dependent in dependents)
            Changed(dependent);
        if (isErrorStatus) SetStatus("", false);
        IsDirty = true;
    }

    private void SetStatus(string message, bool isError)
    {
        IsErrorStatus = isError;
        StatusMessage = message;
    }

    private static string DescribeError(Exception exception, string fallback)
        => exception is SettingsDataException data ? data.Message : fallback;
}
