using ProjectZ.Helpers;
using ProjectZ.Services;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class AddCustomerViewModel : ObservableObject
{
    private readonly PosCustomerDesignItem? existingCustomer;
    private readonly Action<PosCustomerDesignItem> onSaved;
    private readonly Action onCancelled;
    private readonly Func<string, int?, string?>? customValidator;
    private readonly string? customHeaderSubtitle;
    private readonly string? customBannerHint;
    private readonly string? customSaveButtonText;

    private string name = "";
    private string phone = "";
    private string secondaryPhone = "";
    private string address = "";
    private string notes = "";
    private PriceTier defaultPriceTier = PriceTier.Retail;
    private string error = "";

    public IReadOnlyList<PriceTierOption> PriceTierOptions => PosFormatting.PriceTierOptions;

    public bool IsEditMode => existingCustomer is not null;
    public string WindowTitle => IsEditMode ? "تعديل بيانات العميل" : "إضافة عميل جديد";
    public string HeaderTitle => IsEditMode ? "تعديل بيانات العميل" : "إضافة عميل جديد";
    public string HeaderSubtitle => customHeaderSubtitle
        ?? (IsEditMode
            ? $"تحديث ملف العميل ({existingCustomer!.CustomerCodeDisplay})"
            : "إدراج العميل مباشرة في الفاتورة الحالية (مرحلة التصميم)");
    public string BannerHintText => customBannerHint
        ?? (IsEditMode
            ? "يتم تحديث بيانات العميل ومزامنتها فورًا في شاشات المبيعات والصيانة والفواتير داخل الجلسة الحالية."
            : "وضع التصميم (بدون Backend): يتم حفظ بيانات العميل مؤقتًا داخل الجلسة الحالية واختياره تلقائيًا في الفاتورة.");
    public string SaveButtonText => customSaveButtonText
        ?? (IsEditMode ? "حفظ التعديلات" : "حفظ وإدراج في الفاتورة");

    public string Name
    {
        get => name;
        set
        {
            if (name == value) return;
            name = value;
            Changed();
            if (!string.IsNullOrWhiteSpace(Error)) Error = "";
        }
    }

    public string Phone
    {
        get => phone;
        set
        {
            if (phone == value) return;
            phone = value;
            Changed();
            if (!string.IsNullOrWhiteSpace(Error)) Error = "";
        }
    }

    public string SecondaryPhone
    {
        get => secondaryPhone;
        set
        {
            if (secondaryPhone == value) return;
            secondaryPhone = value;
            Changed();
        }
    }

    public string Address
    {
        get => address;
        set
        {
            if (address == value) return;
            address = value;
            Changed();
        }
    }

    public string Notes
    {
        get => notes;
        set
        {
            if (notes == value) return;
            notes = value;
            Changed();
        }
    }

    public PriceTier DefaultPriceTier
    {
        get => defaultPriceTier;
        set
        {
            if (defaultPriceTier == value) return;
            defaultPriceTier = value;
            Changed();
        }
    }

    public string Error
    {
        get => error;
        private set
        {
            if (error == value) return;
            error = value;
            Changed();
        }
    }

    public ActionCommand SaveCommand { get; }
    public ActionCommand CancelCommand { get; }

    public AddCustomerViewModel(
        string? initialQuery,
        Action<PosCustomerDesignItem> onSaved,
        Action onCancelled)
        : this(initialQuery, null, onSaved, onCancelled, null, null, null, null)
    {
    }

    public AddCustomerViewModel(
        string? initialQuery,
        PosCustomerDesignItem? existingCustomer,
        Action<PosCustomerDesignItem> onSaved,
        Action onCancelled,
        Func<string, int?, string?>? customValidator = null,
        string? customHeaderSubtitle = null,
        string? customBannerHint = null,
        string? customSaveButtonText = null)
    {
        this.existingCustomer = existingCustomer;
        this.onSaved = onSaved;
        this.onCancelled = onCancelled;
        this.customValidator = customValidator;
        this.customHeaderSubtitle = customHeaderSubtitle;
        this.customBannerHint = customBannerHint;
        this.customSaveButtonText = customSaveButtonText;

        if (existingCustomer is not null)
        {
            name = existingCustomer.Name;
            phone = existingCustomer.Phone;
            secondaryPhone = existingCustomer.SecondaryPhone;
            address = existingCustomer.Address;
            notes = existingCustomer.Notes;
            defaultPriceTier = existingCustomer.DefaultPriceTier;
        }
        else if (!string.IsNullOrWhiteSpace(initialQuery))
        {
            var trimmed = initialQuery.Trim();
            if (trimmed.Any(char.IsDigit) && trimmed.All(c => char.IsDigit(c) || c is '+' or '-' or ' '))
                phone = trimmed;
            else
                name = trimmed;
        }

        SaveCommand = new ActionCommand(Save);
        CancelCommand = new ActionCommand(this.onCancelled);
    }

    private void Save()
    {
        var trimmedName = Name.Trim();
        var trimmedPhone = Phone.Trim();

        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            Error = "يرجى إدخال اسم العميل.";
            return;
        }

        if (string.IsNullOrWhiteSpace(trimmedPhone))
        {
            Error = "يرجى إدخال رقم هاتف العميل.";
            return;
        }

        var customError = customValidator?.Invoke(trimmedPhone, existingCustomer?.Id);
        if (!string.IsNullOrWhiteSpace(customError))
        {
            Error = customError;
            return;
        }

        if (existingCustomer is not null)
        {
            existingCustomer.Name = trimmedName;
            existingCustomer.Phone = trimmedPhone;
            existingCustomer.SecondaryPhone = SecondaryPhone.Trim();
            existingCustomer.Address = Address.Trim();
            existingCustomer.Notes = Notes.Trim();
            existingCustomer.DefaultPriceTier = DefaultPriceTier;
            onSaved(existingCustomer);
            return;
        }

        var customer = new PosCustomerDesignItem
        {
            Name = trimmedName,
            Phone = trimmedPhone,
            SecondaryPhone = SecondaryPhone.Trim(),
            Address = Address.Trim(),
            Notes = Notes.Trim(),
            DefaultPriceTier = DefaultPriceTier,
            IsWalkIn = false
        };

        onSaved(customer);
    }
}
