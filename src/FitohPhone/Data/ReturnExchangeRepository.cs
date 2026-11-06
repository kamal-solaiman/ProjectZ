using System.Data;
using FitohPhone.Models;
using FitohPhone.Services;
using Microsoft.Data.SqlClient;

namespace FitohPhone.Data;

public sealed class ReturnExchangeRepository(ConnectionSettingsProvider settings)
{
    public async Task<IReadOnlyList<ReturnExchange>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT re.Id, re.ReturnNumber, re.ReturnDate, re.OriginalInvoiceId, si.InvoiceNumber,
                     re.CustomerId, c.Name, c.Phone, re.UserId, u.Username, re.Reason,
                     re.TotalAmount, re.RefundAmount, re.ExchangeSettlementAmount, re.Status,
                     re.Notes, re.CreatedAt
              FROM dbo.ReturnsExchanges re
              LEFT JOIN dbo.SalesInvoices si ON re.OriginalInvoiceId = si.Id
              LEFT JOIN dbo.Customers c ON re.CustomerId = c.Id
              LEFT JOIN dbo.Users u ON re.UserId = u.Id
              ORDER BY re.ReturnDate DESC", conn) { CommandTimeout = 15 };
        var list = new List<ReturnExchange>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(MapReturn(r));
        return list;
    }

    public async Task<ReturnExchange?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT re.Id, re.ReturnNumber, re.ReturnDate, re.OriginalInvoiceId, si.InvoiceNumber,
                     re.CustomerId, c.Name, c.Phone, re.UserId, u.Username, re.Reason,
                     re.TotalAmount, re.RefundAmount, re.ExchangeSettlementAmount, re.Status,
                     re.Notes, re.CreatedAt
              FROM dbo.ReturnsExchanges re
              LEFT JOIN dbo.SalesInvoices si ON re.OriginalInvoiceId = si.Id
              LEFT JOIN dbo.Customers c ON re.CustomerId = c.Id
              LEFT JOIN dbo.Users u ON re.UserId = u.Id
              WHERE re.Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapReturn(r) : null;
    }

    public async Task<int> CreateAsync(ReturnExchange ret, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        SqlTransaction tx = await conn.BeginTransactionAsync(ct);
        try
        {
            await using var cmd = new SqlCommand(
                @"INSERT INTO dbo.ReturnsExchanges (ReturnNumber, ReturnDate, OriginalInvoiceId,
                         CustomerId, UserId, Reason, TotalAmount, RefundAmount,
                         ExchangeSettlementAmount, Status, Notes, CreatedAt)
              OUTPUT INSERTED.Id
              VALUES (@ReturnNumber, @ReturnDate, @OriginalInvoiceId, @CustomerId, @UserId,
                     @Reason, @TotalAmount, @RefundAmount, @ExchangeSettlementAmount,
                     @Status, @Notes, SYSUTCDATETIME())", conn, tx) { CommandTimeout = 15 };
            cmd.Parameters.AddRange(new[]
            {
                new SqlParameter("@ReturnNumber", SqlDbType.NVarChar, 50) { Value = ret.ReturnNumber },
                new SqlParameter("@ReturnDate", SqlDbType.DateTime2) { Value = ret.ReturnDate },
                new SqlParameter("@OriginalInvoiceId", SqlDbType.Int) { Value = (object)ret.OriginalInvoiceId ?? DBNull.Value },
                new SqlParameter("@CustomerId", SqlDbType.Int) { Value = ret.CustomerId },
                new SqlParameter("@UserId", SqlDbType.Int) { Value = ret.UserId },
                new SqlParameter("@Reason", SqlDbType.NVarChar, 500) { Value = (object)ret.Reason ?? DBNull.Value },
                new SqlParameter("@TotalAmount", SqlDbType.Decimal) { Value = ret.TotalAmount },
                new SqlParameter("@RefundAmount", SqlDbType.Decimal) { Value = ret.RefundAmount },
                new SqlParameter("@ExchangeSettlementAmount", SqlDbType.Decimal) { Value = ret.ExchangeSettlementAmount },
                new SqlParameter("@Status", SqlDbType.NVarChar, 30) { Value = ret.Status },
                new SqlParameter("@Notes", SqlDbType.NVarChar, 1000) { Value = (object)ret.Notes ?? DBNull.Value },
            });
            var id = (int)await cmd.ExecuteScalarAsync(ct);

            foreach (var item in ret.Items)
            {
                await using var icmd = new SqlCommand(
                    @"INSERT INTO dbo.ReturnExchangeItems (ReturnId, OriginalInvoiceItemId, ProductId,
                             Quantity, ItemCondition, IsDamaged, UnitPrice, LineTotal,
                             ReplacementProductId, ReplacementQuantity, PriceDifference)
                     VALUES (@ReturnId, @OriginalInvoiceItemId, @ProductId, @Quantity,
                     @ItemCondition, @IsDamaged, @UnitPrice, @LineTotal, @ReplacementProductId,
                     @ReplacementQuantity, @PriceDifference)", conn, tx) { CommandTimeout = 15 };
                icmd.Parameters.AddRange(new[]
                {
                    new SqlParameter("@ReturnId", SqlDbType.Int) { Value = id },
                    new SqlParameter("@OriginalInvoiceItemId", SqlDbType.Int) { Value = (object)item.OriginalInvoiceItemId ?? DBNull.Value },
                    new SqlParameter("@ProductId", SqlDbType.Int) { Value = item.ProductId },
                    new SqlParameter("@Quantity", SqlDbType.Int) { Value = item.Quantity },
                    new SqlParameter("@ItemCondition", SqlDbType.NVarChar, 20) { Value = item.ItemCondition },
                    new SqlParameter("@IsDamaged", SqlDbType.Bit) { Value = item.IsDamaged },
                    new SqlParameter("@UnitPrice", SqlDbType.Decimal) { Value = item.UnitPrice },
                    new SqlParameter("@LineTotal", SqlDbType.Decimal) { Value = item.LineTotal },
                    new SqlParameter("@ReplacementProductId", SqlDbType.Int) { Value = (object)item.ReplacementProductId ?? DBNull.Value },
                    new SqlParameter("@ReplacementQuantity", SqlDbType.Int) { Value = (object)item.ReplacementQuantity ?? DBNull.Value },
                    new SqlParameter("@PriceDifference", SqlDbType.Decimal) { Value = item.PriceDifference },
                });
                await icmd.ExecuteNonQueryAsync(ct);

                // إذا كانت القطعة سليمة، تُضاف للمخزون السليم
                if (!item.IsDamaged)
                {
                    await using var scmd = new SqlCommand(
                        "UPDATE dbo.Products SET CurrentStock = CurrentStock + @Qty, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id", conn, tx) { CommandTimeout = 15 };
                    scmd.Parameters.Add(new SqlParameter("@Qty", SqlDbType.Int) { Value = item.Quantity });
                    scmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = item.ProductId });
                    await scmd.ExecuteNonQueryAsync(ct);
                }
            }

            await tx.CommitAsync(ct);
            return id;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static ReturnExchange MapReturn(SqlDataReader r) => new(
        r.GetInt32(0), r.GetString(1), r.GetDateTime(2),
        r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
        r.IsDBNull(4) ? null : r.GetString(4),
        r.GetInt32(5), r.GetString(6), r.GetString(7),
        r.GetInt32(8), r.GetString(9),
        r.IsDBNull(10) ? null : r.GetString(10),
        r.GetDecimal(11), r.GetDecimal(12), r.GetDecimal(13), r.GetString(14),
        r.IsDBNull(15) ? null : r.GetString(15), r.GetDateTime(16),
        Array.Empty<ReturnExchangeItem>());
}
