namespace FitohPhone.Models;

public sealed record DamagedStock(
    int Id,
    int ProductId,
    string ProductBarcode,
    string ProductName,
    int Quantity,
    string? DamageReason,
    DateTime DamageDate,
    int ReportedByUserId,
    string ReportedByUserName,
    string SourceType,
    int? SourceReferenceId,
    string Disposition,
    DateTime? DispositionDate,
    int? DisposedByUserId,
    string? DisposedByUserName,
    decimal LossValue,
    string? Notes);
