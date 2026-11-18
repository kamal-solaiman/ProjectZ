using ProjectZ.Helpers;
using ProjectZ.Models;

namespace ProjectZ.ViewModels;

public sealed record SupplierCreditSubmission(SupplierCreditItem Credit, decimal Amount, PurchaseInvoiceDesignItem? Target,
    PurchaseReceiptAccount? Account, string Evidence);

/// <summary>Prepare one explicit instruction. The repository posts it once with the versions shown here.</summary>
public sealed class SupplierCreditDialogViewModel : ObservableObject
{
    public string SourceLabel { get; }
    public IReadOnlyList<SupplierCreditItem> Credits { get; }
    public IReadOnlyList<PurchaseInvoiceDesignItem> Debts { get; }
    public IReadOnlyList<PurchaseReceiptAccount> Accounts { get; }
    public SupplierCreditItem? Credit { get; set; }
    public PurchaseInvoiceDesignItem? Target { get; set; }
    public PurchaseReceiptAccount? Account { get; set; }
    public string AmountText { get; set; } = "";
    public string Evidence { get; set; } = "";
    private bool isReceipt;
    public bool IsReceipt
    {
        get => isReceipt;
        set { isReceipt = value; Changed(); Changed(nameof(IsApplication)); Changed(nameof(ConfirmLabel)); }
    }
    public bool IsApplication { get => !isReceipt; set { if (value) IsReceipt = false; } }
    public string ConfirmLabel => IsReceipt ? "تأكيد استلام المبلغ في الحساب المحدد" : "تأكيد المقاصة دون قبض أو صرف";
    public string Error { get; private set; } = "";
    public SupplierCreditSubmission? Result { get; private set; }
    public Action? RequestCloseWithSuccess { get; set; }
    public ActionCommand ConfirmCommand { get; }

    public SupplierCreditDialogViewModel(PurchaseInvoiceDesignItem source, IEnumerable<PurchaseInvoiceDesignItem> invoices, IReadOnlyList<PurchaseReceiptAccount> accounts)
    {
        SourceLabel = $"{source.SupplierName} · {source.InvoiceNumber}";
        Credits = source.CreditNotes.Where(c => c.Available > 0m).ToList();
        Debts = invoices.Where(i => i.SupplierId == source.SupplierId && i.Id != source.Id && i.CanPayRemaining).OrderBy(i => i.IssuedAt).ToList();
        Accounts = accounts;
        Credit = Credits.FirstOrDefault();
        ConfirmCommand = new ActionCommand(Submit);
    }

    private void Submit()
    {
        Error = "";
        if (Credit is null || !Credits.Contains(Credit) || !PosFormatting.TryParseAmount(AmountText, out var amount) || amount <= 0m || amount > Credit.Available || decimal.Round(amount, 2) != amount)
            Error = "اختر إشعارًا وأدخل مبلغًا موجبًا بالقرش لا يتجاوز المتاح منه.";
        else if (IsReceipt && (Account is null || !Accounts.Contains(Account) || string.IsNullOrWhiteSpace(Evidence) || Evidence.Trim().Length > 120))
            Error = "اختر الحساب الذي استلم المبلغ فعليًا وأدخل مرجع إيصال القبض (حتى 120 حرفًا).";
        else if (!IsReceipt && (Target is null || !Debts.Contains(Target) || amount > Target.RemainingPayable))
            Error = "اختر فاتورة أخرى لهذا المورد ولا تتجاوز الدين المتبقي عليها.";
        else
        {
            Result = new(Credit, amount, IsReceipt ? null : Target, IsReceipt ? Account : null, IsReceipt ? Evidence.Trim() : "");
            RequestCloseWithSuccess?.Invoke();
        }
        Changed(nameof(Error));
    }
}
