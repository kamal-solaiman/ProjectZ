using System.Collections.ObjectModel;
using ProjectZ.Data;
using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

/// <summary>
/// Backs the «توريد قطعة غيار» dialog: quantity, purchase unit cost, supplier, reference and note.
/// The dialog validates everything before the screen opens a SQL transaction, so an invalid form
/// never touches the database.
/// </summary>
public sealed class MaintenanceSparePartRestockViewModel : ObservableObject
{
    private string quantityText = "1";
    private string unitCostText = "0";
    private string reference = "";
    private string note = "";
    private SparePartSupplierOption? selectedSupplier;
    private string errorMessage = "";

    public MaintenanceSparePartItem Part { get; }
    public ObservableCollection<SparePartSupplierOption> SupplierOptions { get; }

    public int Quantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public int? SupplierId { get; private set; }
    public string ReferenceText { get; private set; } = "";
    public string NoteText { get; private set; } = "";
    public bool IsConfirmed { get; private set; }
    public Action? RequestClose { get; set; }

    public MaintenanceSparePartRestockViewModel(
        MaintenanceSparePartItem part,
        IEnumerable<SparePartSupplierOption> supplierOptions)
    {
        Part = part;
        SupplierOptions = new ObservableCollection<SparePartSupplierOption>(supplierOptions);
        selectedSupplier = part.SupplierId is int supplierId
            ? SupplierOptions.FirstOrDefault(s => s.Id == supplierId)
            : null;
        unitCostText = PosFormatting.FormatPlainNumber(part.CostPrice);

        ConfirmCommand = new ActionCommand(Confirm);
        AbortCommand = new ActionCommand(Abort);
    }

    public ActionCommand ConfirmCommand { get; }
    public ActionCommand AbortCommand { get; }

    public string PartTitle => $"{Part.CodeDisplay} · {Part.Name}";
    public string AvailableStockDisplay => $"الرصيد الحالي: {Part.Quantity} {Part.Unit}";
    public string ReorderDisplay => Part.ReorderDisplay;
    public string SupplierHint => Part.HasSupplier
        ? $"المورد المفضل المسجل: {Part.SupplierDisplay}"
        : "لم يُربط المورد بعد؛ اختر موردًا لتسجيل التوريد باسمه.";

    public string QuantityText
    {
        get => quantityText;
        set
        {
            if (quantityText == value) return;
            quantityText = value;
            Changed();
            Changed(nameof(PreviewQuantityDisplay));
            Changed(nameof(TotalDisplay));
        }
    }

    public string UnitCostText
    {
        get => unitCostText;
        set
        {
            if (unitCostText == value) return;
            unitCostText = value;
            Changed();
            Changed(nameof(TotalDisplay));
        }
    }

    public string Reference
    {
        get => reference;
        set
        {
            if (reference == value) return;
            reference = value;
            Changed();
        }
    }

    public string Note
    {
        get => note;
        set
        {
            if (note == value) return;
            note = value;
            Changed();
        }
    }

    public SparePartSupplierOption? SelectedSupplier
    {
        get => selectedSupplier;
        set
        {
            if (ReferenceEquals(selectedSupplier, value)) return;
            selectedSupplier = value;
            Changed();
            Changed(nameof(SupplierHint));
        }
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (errorMessage == value) return;
            errorMessage = value;
            Changed();
            Changed(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string PreviewQuantityDisplay
    {
        get
        {
            if (!PosFormatting.TryParseNonNegativeInt(QuantityText, out var qty) || qty <= 0)
                return $"{Part.Quantity} {Part.Unit}";
            return $"{Part.Quantity} ← {Part.Quantity + qty} {Part.Unit}";
        }
    }

    public string TotalDisplay
    {
        get
        {
            var qty = PosFormatting.TryParseNonNegativeInt(QuantityText, out var parsedQty) ? parsedQty : 0;
            var cost = PosFormatting.TryParseAmount(UnitCostText, out var parsedCost) ? parsedCost : 0m;
            return PosFormatting.FormatCurrency(Math.Max(0m, qty * cost));
        }
    }

    private void Confirm()
    {
        if (!PosFormatting.TryParseNonNegativeInt(QuantityText, out var quantity) || quantity <= 0)
        {
            ErrorMessage = "أدخل كمية توريد صحيحة أكبر من صفر.";
            return;
        }

        if (!PosFormatting.TryParseAmount(UnitCostText, out var unitCost) || unitCost < 0m)
        {
            ErrorMessage = "أدخل سعر شراء صحيحًا (صفر أو أكثر).";
            return;
        }

        if (quantity > 1_000_000)
        {
            ErrorMessage = "الكمية كبيرة جدًا؛ أدخل كمية أقل من مليون وحدة.";
            return;
        }

        Quantity = quantity;
        UnitCost = unitCost;
        SupplierId = SelectedSupplier?.Id;
        ReferenceText = Reference.Trim();
        NoteText = Note.Trim();
        IsConfirmed = true;
        ErrorMessage = "";
        RequestClose?.Invoke();
    }

    private void Abort()
    {
        IsConfirmed = false;
        RequestClose?.Invoke();
    }
}
