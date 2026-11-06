using System.Data;
using FitohPhone.Models;
using FitohPhone.Services;
using Microsoft.Data.SqlClient;

namespace FitohPhone.Data;

public sealed class DamagedStockRepository(ConnectionSettingsProvider settings)
{
    public async Task<IReadOnlyList<DamagedStock>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT ds.Id, ds.ProductId, p.Barcode, p.NameAr, ds.Quantity, ds.DamageReason,
                     ds.DamageDate, ds.ReportedByUserId, u1.Username, ds.SourceType,
                     ds.SourceReferenceId, ds.Disposition, ds.DispositionDate,
                     ds.DisposedByUserId, u2.Username, ds.LossValue, ds.Notes
              FROM dbo.DamagedStock ds
              LEFT JOIN dbo.Products p ON ds.ProductId = p.Id
              LEFT JOIN dbo.Users u1 ON ds.ReportedByUserId = u1.Id
              LEFT JOIN dbo.Users u2 ON ds.DisposedByUserId = u2.Id
              ORDER BY ds.DamageDate DESC", conn) { CommandTimeout = 15 };
        var list = new List<DamagedStock>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new DamagedStock(
                r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? "" : r.GetString(2),
                r.IsDBNull(3) ? "" : r.GetString(3), r.GetInt32(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.GetDateTime(6),
                r.GetInt32(7), r.GetString(8), r.GetString(9),
                r.IsDBNull(10) ? (int?)null : r.GetInt32(10), r.GetString(11),
                r.IsDBNull(12) ? null : r.GetDateTime(12),
                r.IsDBNull(13) ? (int?)null : r.GetInt32(13),
                r.IsDBNull(14) ? null : r.GetString(14),
                r.GetDecimal(15), r.IsDBNull(16) ? null : r.GetString(16)));
        return list;
    }

    public async Task<int> AddAsync(DamagedStock item, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"INSERT INTO dbo.DamagedStock (ProductId, Quantity, DamageReason, DamageDate,
                     ReportedByUserId, SourceType, SourceReferenceId, Disposition,
                     LossValue, Notes, CreatedAt)
              OUTPUT INSERTED.Id
              VALUES (@ProductId, @Quantity, @DamageReason, @DamageDate, @ReportedByUserId,
                     @SourceType, @SourceReferenceId, @Disposition, @LossValue, @Notes, SYSUTCDATETIME())",
            conn) { CommandTimeout = 15 };
        cmd.Parameters.AddRange(new[]
        {
            new SqlParameter("@ProductId", SqlDbType.Int) { Value = item.ProductId },
            new SqlParameter("@Quantity", SqlDbType.Int) { Value = item.Quantity },
            new SqlParameter("@DamageReason", SqlDbType.NVarChar, 500) { Value = (object)item.DamageReason ?? DBNull.Value },
            new SqlParameter("@DamageDate", SqlDbType.DateTime2) { Value = item.DamageDate },
            new SqlParameter("@ReportedByUserId", SqlDbType.Int) { Value = item.ReportedByUserId },
            new SqlParameter("@SourceType", SqlDbType.NVarChar, 30) { Value = item.SourceType },
            new SqlParameter("@SourceReferenceId", SqlDbType.Int) { Value = (object)item.SourceReferenceId ?? DBNull.Value },
            new SqlParameter("@Disposition", SqlDbType.NVarChar, 30) { Value = item.Disposition },
            new SqlParameter("@LossValue", SqlDbType.Decimal) { Value = item.LossValue },
            new SqlParameter("@Notes", SqlDbType.NVarChar, 1000) { Value = (object)item.Notes ?? DBNull.Value },
        });
        return (int)await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<bool> UpdateDispositionAsync(int id, string disposition, int? disposedByUserId, DateTime? dispositionDate, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"UPDATE dbo.DamagedStock SET Disposition=@Disposition, DisposedByUserId=@DisposedBy,
                     DispositionDate=@DispositionDate WHERE Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        cmd.Parameters.Add(new SqlParameter("@Disposition", SqlDbType.NVarChar, 30) { Value = disposition });
        cmd.Parameters.Add(new SqlParameter("@DisposedBy", SqlDbType.Int) { Value = (object)disposedByUserId ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@DispositionDate", SqlDbType.DateTime2) { Value = (object)dispositionDate ?? DBNull.Value });
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }
}
