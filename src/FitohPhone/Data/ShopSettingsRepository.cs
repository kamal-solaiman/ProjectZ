using System.Data;
using FitohPhone.Models;
using FitohPhone.Services;
using Microsoft.Data.SqlClient;

namespace FitohPhone.Data;

public sealed class ShopSettingsRepository(ConnectionSettingsProvider settings)
{
    public async Task<ShopSetting?> GetAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT Id, ShopNameAr, ShopNameEn, Phone1, Phone2, WhatsAppNumber, Address,
                     TaxNumber, CommercialRegisterNumber, LogoPath, ReceiptHeaderText,
                     ReceiptFooterText, DefaultCurrency, ThermalPrinterName, BarcodePrinterName,
                     AutoPrintInvoice, LowStockAlert, TransferLimitWarning, CreatedAt, UpdatedAt
              FROM dbo.ShopSettings WHERE Id=1", conn) { CommandTimeout = 15 };
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task<bool> UpdateAsync(ShopSetting setting, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"UPDATE dbo.ShopSettings SET ShopNameAr=@ShopNameAr, ShopNameEn=@ShopNameEn,
                     Phone1=@Phone1, Phone2=@Phone2, WhatsAppNumber=@WhatsAppNumber,
                     Address=@Address, TaxNumber=@TaxNumber, CommercialRegisterNumber=@CommercialRegisterNumber,
                     LogoPath=@LogoPath, ReceiptHeaderText=@ReceiptHeaderText,
                     ReceiptFooterText=@ReceiptFooterText, DefaultCurrency=@DefaultCurrency,
                     ThermalPrinterName=@ThermalPrinterName, BarcodePrinterName=@BarcodePrinterName,
                     AutoPrintInvoice=@AutoPrintInvoice, LowStockAlert=@LowStockAlert,
                     TransferLimitWarning=@TransferLimitWarning, UpdatedAt=SYSUTCDATETIME()
              WHERE Id=1", conn) { CommandTimeout = 15 };
        cmd.Parameters.AddRange(new[]
        {
            new SqlParameter("@ShopNameAr", SqlDbType.NVarChar, 200) { Value = setting.ShopNameAr },
            new SqlParameter("@ShopNameEn", SqlDbType.NVarChar, 200) { Value = setting.ShopNameEn },
            new SqlParameter("@Phone1", SqlDbType.NVarChar, 20) { Value = setting.Phone1 },
            new SqlParameter("@Phone2", SqlDbType.NVarChar, 20) { Value = (object)setting.Phone2 ?? DBNull.Value },
            new SqlParameter("@WhatsAppNumber", SqlDbType.NVarChar, 20) { Value = (object)setting.WhatsAppNumber ?? DBNull.Value },
            new SqlParameter("@Address", SqlDbType.NVarChar, 500) { Value = (object)setting.Address ?? DBNull.Value },
            new SqlParameter("@TaxNumber", SqlDbType.NVarChar, 50) { Value = (object)setting.TaxNumber ?? DBNull.Value },
            new SqlParameter("@CommercialRegisterNumber", SqlDbType.NVarChar, 50) { Value = (object)setting.CommercialRegisterNumber ?? DBNull.Value },
            new SqlParameter("@LogoPath", SqlDbType.NVarChar, 500) { Value = (object)setting.LogoPath ?? DBNull.Value },
            new SqlParameter("@ReceiptHeaderText", SqlDbType.NVarChar, 1000) { Value = (object)setting.ReceiptHeaderText ?? DBNull.Value },
            new SqlParameter("@ReceiptFooterText", SqlDbType.NVarChar, 1000) { Value = (object)setting.ReceiptFooterText ?? DBNull.Value },
            new SqlParameter("@DefaultCurrency", SqlDbType.NVarChar, 20) { Value = setting.DefaultCurrency },
            new SqlParameter("@ThermalPrinterName", SqlDbType.NVarChar, 200) { Value = (object)setting.ThermalPrinterName ?? DBNull.Value },
            new SqlParameter("@BarcodePrinterName", SqlDbType.NVarChar, 200) { Value = (object)setting.BarcodePrinterName ?? DBNull.Value },
            new SqlParameter("@AutoPrintInvoice", SqlDbType.Bit) { Value = setting.AutoPrintInvoice },
            new SqlParameter("@LowStockAlert", SqlDbType.Bit) { Value = setting.LowStockAlert },
            new SqlParameter("@TransferLimitWarning", SqlDbType.Bit) { Value = setting.TransferLimitWarning },
        });
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    private static ShopSetting Map(SqlDataReader r) => new(
        r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3),
        r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5),
        r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7),
        r.IsDBNull(8) ? null : r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9),
        r.IsDBNull(10) ? null : r.GetString(10), r.IsDBNull(11) ? null : r.GetString(11),
        r.GetString(12), r.IsDBNull(13) ? null : r.GetString(13),
        r.IsDBNull(14) ? null : r.GetString(14), r.GetBoolean(15), r.GetBoolean(16),
        r.GetBoolean(17), r.GetDateTime(18),
        r.IsDBNull(19) ? null : r.GetDateTime(19));
}
