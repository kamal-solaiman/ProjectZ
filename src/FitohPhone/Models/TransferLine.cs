namespace FitohPhone.Models;

public sealed record TransferLine(
    int Id,
    string Provider,
    string ProviderAr,
    string PhoneNumber,
    decimal CurrentBalance,
    decimal DailyLimit,
    decimal MonthlyLimit,
    decimal DailyUsed,
    decimal MonthlyUsed,
    DateOnly? LastDailyResetDate,
    DateOnly? LastMonthlyResetDate,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record TransferLineSummary(
    int Id,
    string ProviderAr,
    string PhoneNumber,
    decimal CurrentBalance,
    decimal DailyLimit,
    decimal MonthlyLimit,
    decimal DailyUsed,
    decimal MonthlyUsed,
    decimal DailyRemaining,
    decimal MonthlyRemaining,
    bool IsActive);

public sealed record TransferTransaction(
    int Id,
    DateTime TransferDate,
    int? FromLineId,
    string? FromPhoneNumber,
    int? ToLineId,
    string? ToPhoneNumber,
    decimal Amount,
    decimal Commission,
    string TransferType,
    string TransferTypeAr,
    string Status,
    string? ReferenceNumber,
    int UserId,
    string UserName,
    string? Notes,
    DateTime CreatedAt);
