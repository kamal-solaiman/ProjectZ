using System.Data;
using FitohPhone.Models;
using FitohPhone.Services;
using Microsoft.Data.SqlClient;

namespace FitohPhone.Data;

public sealed class TransferRepository(ConnectionSettingsProvider settings)
{
    public async Task<IReadOnlyList<TransferLine>> GetLinesAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT Id, Provider, ProviderAr, PhoneNumber, CurrentBalance, DailyLimit,
                     MonthlyLimit, DailyUsed, MonthlyUsed, LastDailyResetDate,
                     LastMonthlyResetDate, IsActive, CreatedAt, UpdatedAt
              FROM dbo.TransferLines ORDER BY ProviderAr, PhoneNumber", conn) { CommandTimeout = 15 };
        var list = new List<TransferLine>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(MapLine(r));
        return list;
    }

    public async Task<TransferLine?> GetLineByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT Id, Provider, ProviderAr, PhoneNumber, CurrentBalance, DailyLimit,
                     MonthlyLimit, DailyUsed, MonthlyUsed, LastDailyResetDate,
                     LastMonthlyResetDate, IsActive, CreatedAt, UpdatedAt
              FROM dbo.TransferLines WHERE Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapLine(r) : null;
    }

    public async Task<int> AddLineAsync(TransferLine line, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"INSERT INTO dbo.TransferLines (Provider, ProviderAr, PhoneNumber, CurrentBalance,
                     DailyLimit, MonthlyLimit, DailyUsed, MonthlyUsed, LastDailyResetDate,
                     LastMonthlyResetDate, IsActive, CreatedAt, UpdatedAt)
              OUTPUT INSERTED.Id
              VALUES (@Provider, @ProviderAr, @PhoneNumber, @CurrentBalance, @DailyLimit,
                     @MonthlyLimit, 0, 0, SYSUTCDATETIME(), SYSUTCDATETIME(), 1, SYSUTCDATETIME(), SYSUTCDATETIME())", conn) { CommandTimeout = 15 };
        cmd.Parameters.AddRange(new[]
        {
            new SqlParameter("@Provider", SqlDbType.NVarChar, 30) { Value = line.Provider },
            new SqlParameter("@ProviderAr", SqlDbType.NVarChar, 50) { Value = line.ProviderAr },
            new SqlParameter("@PhoneNumber", SqlDbType.NVarChar, 20) { Value = line.PhoneNumber },
            new SqlParameter("@CurrentBalance", SqlDbType.Decimal) { Value = line.CurrentBalance },
            new SqlParameter("@DailyLimit", SqlDbType.Decimal) { Value = line.DailyLimit },
            new SqlParameter("@MonthlyLimit", SqlDbType.Decimal) { Value = line.MonthlyLimit },
        });
        return (int)await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<bool> UpdateLineAsync(TransferLine line, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"UPDATE dbo.TransferLines SET Provider=@Provider, ProviderAr=@ProviderAr,
                     PhoneNumber=@PhoneNumber, CurrentBalance=@CurrentBalance,
                     DailyLimit=@DailyLimit, MonthlyLimit=@MonthlyLimit,
                     IsActive=@IsActive, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.AddRange(new[]
        {
            new SqlParameter("@Id", SqlDbType.Int) { Value = line.Id },
            new SqlParameter("@Provider", SqlDbType.NVarChar, 30) { Value = line.Provider },
            new SqlParameter("@ProviderAr", SqlDbType.NVarChar, 50) { Value = line.ProviderAr },
            new SqlParameter("@PhoneNumber", SqlDbType.NVarChar, 20) { Value = line.PhoneNumber },
            new SqlParameter("@CurrentBalance", SqlDbType.Decimal) { Value = line.CurrentBalance },
            new SqlParameter("@DailyLimit", SqlDbType.Decimal) { Value = line.DailyLimit },
            new SqlParameter("@MonthlyLimit", SqlDbType.Decimal) { Value = line.MonthlyLimit },
            new SqlParameter("@IsActive", SqlDbType.Bit) { Value = line.IsActive },
        });
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> DeleteLineAsync(int id, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand("DELETE FROM dbo.TransferLines WHERE Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> UpdateBalanceAsync(int lineId, decimal amountChange, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"UPDATE dbo.TransferLines SET CurrentBalance = CurrentStock + @Amt, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@Amt", SqlDbType.Decimal) { Value = amountChange });
        cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = lineId });
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<IReadOnlyList<TransferTransaction>> GetTransactionsAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT tt.Id, tt.TransferDate, tt.FromLineId, fl.PhoneNumber, tt.ToLineId,
                     tl.PhoneNumber, tt.Amount, tt.Commission, tt.TransferType, tt.TransferTypeAr,
                     tt.Status, tt.ReferenceNumber, tt.UserId, u.Username, tt.Notes, tt.CreatedAt
              FROM dbo.TransferTransactions tt
              LEFT JOIN dbo.TransferLines fl ON tt.FromLineId = fl.Id
              LEFT JOIN dbo.TransferLines tl ON tt.ToLineId = tl.Id
              LEFT JOIN dbo.Users u ON tt.UserId = u.Id
              ORDER BY tt.TransferDate DESC", conn) { CommandTimeout = 15 };
        var list = new List<TransferTransaction>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(MapTransaction(r));
        return list;
    }

    public async Task<int> CreateTransactionAsync(TransferTransaction txn, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"INSERT INTO dbo.TransferTransactions (TransferDate, FromLineId, ToLineId, ToPhoneNumber,
                     Amount, Commission, TransferType, TransferTypeAr, Status, ReferenceNumber,
                     UserId, Notes, CreatedAt)
              OUTPUT INSERTED.Id
              VALUES (@TransferDate, @FromLineId, @ToLineId, @ToPhoneNumber, @Amount, @Commission,
                     @TransferType, @TransferTypeAr, @Status, @ReferenceNumber, @UserId, @Notes, SYSUTCDATETIME())",
            conn) { CommandTimeout = 15 };
        cmd.Parameters.AddRange(new[]
        {
            new SqlParameter("@TransferDate", SqlDbType.DateTime2) { Value = txn.TransferDate },
            new SqlParameter("@FromLineId", SqlDbType.Int) { Value = (object)txn.FromLineId ?? DBNull.Value },
            new SqlParameter("@ToLineId", SqlDbType.Int) { Value = (object)txn.ToLineId ?? DBNull.Value },
            new SqlParameter("@ToPhoneNumber", SqlDbType.NVarChar, 20) { Value = (object)txn.ToPhoneNumber ?? DBNull.Value },
            new SqlParameter("@Amount", SqlDbType.Decimal) { Value = txn.Amount },
            new SqlParameter("@Commission", SqlDbType.Decimal) { Value = txn.Commission },
            new SqlParameter("@TransferType", SqlDbType.NVarChar, 20) { Value = txn.TransferType },
            new SqlParameter("@TransferTypeAr", SqlDbType.NVarChar, 50) { Value = txn.TransferTypeAr },
            new SqlParameter("@Status", SqlDbType.NVarChar, 30) { Value = txn.Status },
            new SqlParameter("@ReferenceNumber", SqlDbType.NVarChar, 100) { Value = (object)txn.ReferenceNumber ?? DBNull.Value },
            new SqlParameter("@UserId", SqlDbType.Int) { Value = txn.UserId },
            new SqlParameter("@Notes", SqlDbType.NVarChar, 500) { Value = (object)txn.Notes ?? DBNull.Value },
        });
        return (int)await cmd.ExecuteScalarAsync(ct);
    }

    private static TransferLine MapLine(SqlDataReader r) => new(
        r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3),
        r.GetDecimal(4), r.GetDecimal(5), r.GetDecimal(6), r.GetDecimal(7), r.GetDecimal(8),
        r.IsDBNull(9) ? null : DateOnly.FromDateTime(r.GetDateTime(9)),
        r.IsDBNull(10) ? null : DateOnly.FromDateTime(r.GetDateTime(10)),
        r.GetBoolean(11), r.GetDateTime(12),
        r.IsDBNull(13) ? null : r.GetDateTime(13));

    private static TransferTransaction MapTransaction(SqlDataReader r) => new(
        r.GetInt32(0), r.GetDateTime(1),
        r.IsDBNull(2) ? (int?)null : r.GetInt32(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.IsDBNull(4) ? (int?)null : r.GetInt32(4),
        r.IsDBNull(5) ? null : r.GetString(5),
        r.GetDecimal(6), r.GetDecimal(7), r.GetString(8), r.GetString(9),
        r.GetString(10), r.IsDBNull(11) ? null : r.GetString(11),
        r.GetInt32(12), r.GetString(13),
        r.IsDBNull(14) ? null : r.GetString(14), r.GetDateTime(15));
}
