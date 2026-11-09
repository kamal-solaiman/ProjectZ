using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed class AddCustomerViewModel : ObservableObject
{
    private readonly Action<PosCustomerDesignItem> onSaved;
    private readonly Action onCancelled;

    private string name = "";
    private string phone = "";
    private string secondaryPhone = "";
    private string address = "";
    private string notes = "";
    private PriceTier defaultPriceTier = PriceTier.Retail;
    private string error = "";

    public IReadOnlyList<PriceTierOption> PriceTierOptions => PosFormatting.PriceTierOptions;

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
    {
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
            Error = "يرجى إدخال اسم العميل.";
            return;
        }

        if (string.IsNullOrWhiteSpace(trimmedPhone))
        {
            Error = "يرجى إدخال رقم هاتف العميل.";
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
