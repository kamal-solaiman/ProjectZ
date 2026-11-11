using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class AddSupplierViewModel : ObservableObject
{
    private readonly SupplierDesignItem? existingSupplier;
    private readonly Func<string, bool>? isDuplicateSupplier;
    private readonly Func<string, int?, string?>? customValidator;
    private readonly Action<SupplierDesignItem> onSaved;
    private readonly Action onCancelled;
    private readonly string? customHeaderSubtitle;
    private readonly string? customBannerHint;
    private readonly string? customSaveButtonText;

    private string name = "";
    private string phone = "";
    private string secondaryPhone = "";
    private string companyName = "";
    private string specialization = "";
    private string address = "";
    private string vodafoneCashNumber = "";
    private string instaPayAddress = "";
    private string notes = "";
    private string error = "";

    public bool IsEditMode => existingSupplier is not null;
    public string WindowTitle => IsEditMode ? "ProjectZ | تعديل بيانات المورد" : "ProjectZ | إضافة مورد جديد";
    public string HeaderTitle => IsEditMode ? "تعديل بيانات المورد" : "إضافة مورد جديد";
    public string HeaderSubtitle => customHeaderSubtitle ?? (IsEditMode
        ? $"تحديث الملف وبيانات التحويل ({existingSupplier!.SupplierCodeDisplay})"
        : "حفظ بيانات المورد وربطه مباشرة بفاتورة الشراء الحالية");
    public string BannerHintText => customBannerHint ?? (IsEditMode
        ? "تُحدّث البيانات في دليل الموردين وتُزامن معلومات الاتصال على فواتير المشتريات المرتبطة."
        : "مرحلة التصميم (بدون Backend): يُضاف المورد للقائمة المشتركة ويُربط بفاتورة الشراء عند الحفظ.");
    public string SaveButtonText => customSaveButtonText ?? (IsEditMode ? "حفظ التعديلات" : "حفظ وإدراج في فاتورة الشراء");

    public string Name { get => name; set { if (name == value) return; name = value; Changed(); ClearError(); } }
    public string Phone { get => phone; set { if (phone == value) return; phone = value; Changed(); ClearError(); } }
    public string SecondaryPhone { get => secondaryPhone; set { if (secondaryPhone == value) return; secondaryPhone = value; Changed(); } }
    public string CompanyName { get => companyName; set { if (companyName == value) return; companyName = value; Changed(); } }
    public string Specialization { get => specialization; set { if (specialization == value) return; specialization = value; Changed(); } }
    public string Address { get => address; set { if (address == value) return; address = value; Changed(); } }
    public string VodafoneCashNumber { get => vodafoneCashNumber; set { if (vodafoneCashNumber == value) return; vodafoneCashNumber = value; Changed(); ClearError(); } }
    public string InstaPayAddress { get => instaPayAddress; set { if (instaPayAddress == value) return; instaPayAddress = value; Changed(); ClearError(); } }
    public string Notes { get => notes; set { if (notes == value) return; notes = value; Changed(); } }

    public string Error
    {
        get => error;
        private set { if (error == value) return; error = value; Changed(); }
    }

    public ActionCommand SaveCommand { get; }
    public ActionCommand CancelCommand { get; }

    public AddSupplierViewModel(
        string? initialQuery,
        Func<string, bool>? isDuplicateSupplier,
        Action<SupplierDesignItem> onSaved,
        Action onCancelled)
        : this(initialQuery, null, isDuplicateSupplier, onSaved, onCancelled)
    {
    }

    public AddSupplierViewModel(
        string? initialQuery,
        SupplierDesignItem? existingSupplier,
        Func<string, bool>? isDuplicateSupplier,
        Action<SupplierDesignItem> onSaved,
        Action onCancelled,
        Func<string, int?, string?>? customValidator = null,
        string? customHeaderSubtitle = null,
        string? customBannerHint = null,
        string? customSaveButtonText = null)
    {
        this.existingSupplier = existingSupplier;
        this.isDuplicateSupplier = isDuplicateSupplier;
        this.onSaved = onSaved;
        this.onCancelled = onCancelled;
        this.customValidator = customValidator;
        this.customHeaderSubtitle = customHeaderSubtitle;
        this.customBannerHint = customBannerHint;
        this.customSaveButtonText = customSaveButtonText;

        if (existingSupplier is not null)
        {
            name = existingSupplier.Name;
            phone = existingSupplier.Phone;
            secondaryPhone = existingSupplier.SecondaryPhone;
            companyName = existingSupplier.CompanyName;
            specialization = existingSupplier.Specialization;
            address = existingSupplier.Address;
            vodafoneCashNumber = existingSupplier.VodafoneCashNumber;
            instaPayAddress = existingSupplier.InstaPayAddress;
            notes = existingSupplier.Notes;
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

    private void ClearError()
    {
        if (!string.IsNullOrWhiteSpace(Error)) Error = "";
    }

    private void Save()
    {
        var trimmedName = Name.Trim();
        var trimmedPhone = Phone.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName)) { Error = "يرجى إدخال اسم المورد."; return; }
        if (string.IsNullOrWhiteSpace(trimmedPhone)) { Error = "يرجى إدخال رقم هاتف المورد."; return; }
        if (isDuplicateSupplier is not null && isDuplicateSupplier(trimmedName))
        {
            Error = $"المورد «{trimmedName}» مسجل بالفعل في قائمة الموردين.";
            return;
        }

        var customError = customValidator?.Invoke(trimmedPhone, existingSupplier?.Id);
        if (!string.IsNullOrWhiteSpace(customError)) { Error = customError; return; }

        if (!string.IsNullOrWhiteSpace(VodafoneCashNumber) && NormalizeDigits(VodafoneCashNumber).Length < 8)
        {
            Error = "يرجى إدخال رقم فودافون كاش صحيح أو ترك الحقل فارغًا.";
            return;
        }

        if (existingSupplier is not null)
        {
            ApplyTo(existingSupplier);
            onSaved(existingSupplier);
            return;
        }

        var supplier = new SupplierDesignItem();
        ApplyTo(supplier);
        onSaved(supplier);
    }

    private void ApplyTo(SupplierDesignItem supplier)
    {
        supplier.Name = Name.Trim();
        supplier.Phone = Phone.Trim();
        supplier.SecondaryPhone = SecondaryPhone.Trim();
        supplier.CompanyName = CompanyName.Trim();
        supplier.Specialization = Specialization.Trim();
        supplier.Address = Address.Trim();
        supplier.VodafoneCashNumber = VodafoneCashNumber.Trim();
        supplier.InstaPayAddress = InstaPayAddress.Trim();
        supplier.Notes = Notes.Trim();
    }

    private static string NormalizeDigits(string value) => new(value.Where(char.IsDigit).ToArray());
}
