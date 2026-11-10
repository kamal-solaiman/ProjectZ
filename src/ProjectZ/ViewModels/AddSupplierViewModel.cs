using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class AddSupplierViewModel : ObservableObject
{
    private readonly Func<string, bool>? isDuplicateSupplier;
    private readonly Action<SupplierDesignItem> onSaved;
    private readonly Action onCancelled;

    private string name = "";
    private string phone = "";
    private string secondaryPhone = "";
    private string companyName = "";
    private string address = "";
    private string notes = "";
    private string error = "";

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

    public string CompanyName
    {
        get => companyName;
        set
        {
            if (companyName == value) return;
            companyName = value;
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

    public AddSupplierViewModel(
        string? initialQuery,
        Func<string, bool>? isDuplicateSupplier,
        Action<SupplierDesignItem> onSaved,
        Action onCancelled)
    {
        this.isDuplicateSupplier = isDuplicateSupplier;
        this.onSaved = onSaved;
        this.onCancelled = onCancelled;

        if (!string.IsNullOrWhiteSpace(initialQuery))
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
            Error = "يرجى إدخال اسم المورد.";
            return;
        }

        if (string.IsNullOrWhiteSpace(trimmedPhone))
        {
            Error = "يرجى إدخال رقم هاتف المورد.";
            return;
        }

        if (isDuplicateSupplier is not null && isDuplicateSupplier(trimmedName))
        {
            Error = $"المورد «{trimmedName}» مسجل بالفعل في قائمة الموردين.";
            return;
        }

        var supplier = new SupplierDesignItem
        {
            Id = 0,
            Name = trimmedName,
            Phone = trimmedPhone,
            SecondaryPhone = SecondaryPhone.Trim(),
            CompanyName = CompanyName.Trim(),
            Address = Address.Trim(),
            Notes = Notes.Trim()
        };

        onSaved(supplier);
    }
}
