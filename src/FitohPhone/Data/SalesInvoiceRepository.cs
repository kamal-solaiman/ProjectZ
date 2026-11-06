using System.Data;
using FitohPhone.Models;
using FitohPhone.Services;
using Microsoft.Data.SqlClient;

namespace FitohPhone.Data;

public sealed class SalesInvoiceRepository(ConnectionSettingsProvider settings)
{
    public async Task<IReadOnlyList<SalesInvoiceListItem>> GetListAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT si.Id, si.InvoiceNumber, si.InvoiceDate, c.Name, c.Phone, u.Username,
                     si.GrandTotal, si.PaidAmount, si.RemainingAmount, si.PaymentStatus, si.IsCancelled
              FROM dbo.SalesInvoices si
              LEFT JOIN dbo.Customers c ON si.CustomerId = c.Id
              LEFT JOIN dbo.Users u ON si.UserId = u.Id
              ORDER BY si.InvoiceDate DESC", conn) { CommandTimeout = 15 };
        var list = new List<SalesInvoiceListItem>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new SalesInvoiceListItem(
                r.GetInt32(0), r.GetString(1), r.GetDateTime(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.GetString(5), r.GetDecimal(6), r.GetDecimal(7),
                r.GetDecimal(8), r.GetString(9), r.GetBoolean(10)));
        return list;
    }

    public async Task<SalesInvoice?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT si.Id, si.InvoiceNumber, si.InvoiceDate, si.CustomerId, c.Name, c.Phone,
                     si.UserId, u.Username, si.SubTotal, si.DiscountAmount, si.TaxAmount,
                     si.GrandTotal, si.PaidAmount, si.RemainingAmount, si.PaymentStatus,
                     si.PriceTier, si.Notes, si.IsCancelled, si.CancelledAt, si.CreatedAt
              FROM dbo.SalesInvoices si
              LEFT JOIN dbo.Customers c ON si.CustomerId = c.Id
              LEFT JOIN dbo.Users u ON si.UserId = u.Id
              WHERE si.Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var invoice = MapInvoiceHeader(r);
        invoice = invoice with
        {
            Items = await GetItemsAsync(invoice.Id, ct),
            Payments = await GetPaymentsAsync(invoice.Id, ct)
        };
        return invoice;
    }

    public async Task<int> CreateAsync(SalesInvoice invoice, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            await using var cmd = new SqlCommand(
                @"INSERT INTO dbo.SalesInvoices (InvoiceNumber, InvoiceDate, CustomerId, UserId,
                         SubTotal, DiscountAmount, TaxAmount, GrandTotal, PaidAmount,
                         RemainingAmount, PaymentStatus, PriceTier, Notes, IsCancelled, CreatedAt)
              OUTPUT INSERTED.Id
              VALUES (@InvoiceNumber, @InvoiceDate, @CustomerId, @UserId,
                         @SubTotal, @DiscountAmount, @TaxAmount, @GrandTotal, @PaidAmount,
                         @RemainingAmount, @PaymentStatus, @PriceTier, @Notes, 0, SYSUTCDATETIME())",
                conn, tx) { CommandTimeout = 15 };
            cmd.Parameters.AddRange(new[]
            {
                new SqlParameter("@InvoiceNumber", SqlDbType.NVarChar, 50) { Value = invoice.InvoiceNumber },
                new SqlParameter("@InvoiceDate", SqlDbType.DateTime2) { Value = invoice.InvoiceDate },
                new SqlParameter("@CustomerId", SqlDbType.Int) { Value = (object)invoice.CustomerId ?? DBNull.Value },
                new SqlParameter("@UserId", SqlDbType.Int) { Value = invoice.UserId },
                new SqlParameter("@SubTotal", SqlDbType.Decimal) { Value = invoice.SubTotal },
                new SqlParameter("@DiscountAmount", SqlDbType.Decimal) { Value = invoice.DiscountAmount },
                new SqlParameter("@TaxAmount", SqlDbType.Decimal) { Value = invoice.TaxAmount },
                new SqlParameter("@GrandTotal", SqlDbType.Decimal) { Value = invoice.GrandTotal },
                new SqlParameter("@PaidAmount", SqlDbType.Decimal) { Value = invoice.PaidAmount },
                new SqlParameter("@RemainingAmount", SqlDbType.Decimal) { Value = invoice.RemainingAmount },
                new SqlParameter("@PaymentStatus", SqlDbType.NVarChar, 20) { Value = invoice.PaymentStatus },
                new SqlParameter("@PriceTier", SqlDbType.NVarChar, 20) { Value = invoice.PriceTier },
                new SqlParameter("@Notes", SqlDbType.NVarChar, 1000) { Value = (object)invoice.Notes ?? DBNull.Value },
            });
            var id = (int)await cmd.ExecuteScalarAsync(ct);

            foreach (var item in invoice.Items)
            {
                await using var icmd = new SqlCommand(
                    @"INSERT INTO dbo.SalesInvoiceItems (InvoiceId, ProductId, Quantity, UnitPrice,
                             DiscountAmount, LineTotal, PriceTierUsed)
                     VALUES (@InvoiceId, @ProductId, @Quantity, @UnitPrice, @DiscountAmount, @LineTotal, @PriceTierUsed)",
                    conn, tx) { CommandTimeout = 15 };
                icmd.Parameters.AddRange(new[]
                {
                    new SqlParameter("@InvoiceId", SqlDbType.Int) { Value = id },
                    new SqlParameter("@ProductId", SqlDbType.Int) { Value = item.ProductId },
                    new SqlParameter("@Quantity", SqlDbType.Int) { Value = item.Quantity },
                    new SqlParameter("@UnitPrice", SqlDbType.Decimal) { Value = item.UnitPrice },
                    new SqlParameter("@DiscountAmount", SqlDbType.Decimal) { Value = item.DiscountAmount },
                    new SqlParameter("@LineTotal", SqlDbType.Decimal) { Value = item.LineTotal },
                    new SqlParameter("@PriceTierUsed", SqlDbType.NVarChar, 20) { Value = item.PriceTierUsed },
                });
                await icmd.ExecuteNonQueryAsync(ct);
            }

            foreach (var pay in invoice.Payments)
            {
                await using var pcmd = new SqlCommand(
                    @"INSERT INTO dbo.SalesInvoicePayments (InvoiceId, PaymentMethod, PaymentMethodAr,
                             Amount, ReferenceNumber, PaidAt)
                     VALUES (@InvoiceId, @PaymentMethod, @PaymentMethodAr, @Amount, @ReferenceNumber, @PaidAt)",
                    conn, tx) { CommandTimeout = 15 };
                pcmd.Parameters.AddRange(new[]
                {
                    new SqlParameter("@InvoiceId", SqlDbType.Int) { Value = id },
                    new SqlParameter("@PaymentMethod", SqlDbType.NVarChar, 50) { Value = pay.PaymentMethod },
                    new SqlParameter("@PaymentMethodAr", SqlDbType.NVarChar, 50) { Value = pay.PaymentMethodAr },
                    new SqlParameter("@Amount", SqlDbType.Decimal) { Value = pay.Amount },
                    new SqlParameter("@ReferenceNumber", SqlDbType.NVarChar, 100) { Value = (object)pay.ReferenceNumber ?? DBNull.Value },
                    new SqlParameter("@PaidAt", SqlDbType.DateTime2) { Value = pay.PaidAt },
                });
                await pcmd.ExecuteNonQueryAsync(ct);
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

    public async Task<bool> CancelAsync(int invoiceId, int? cancelledByUserId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"UPDATE dbo.SalesInvoices SET IsCancelled=1, CancelledAt=SYSUTCDATETIME(), CancelledByUserId=@CancelledBy
              WHERE Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = invoiceId });
        cmd.Parameters.Add(new SqlParameter("@CancelledBy", SqlDbType.Int) { Value = (object)cancelledByUserId ?? DBNull.Value });
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    private static SalesInvoiceListItem MapListItem(SqlDataReader r) => new(
        r.GetInt32(0), r.GetString(1), r.GetDateTime(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.IsDBNull(4) ? null : r.GetString(4),
        r.GetString(5), r.GetDecimal(6), r.GetDecimal(7),
        r.GetDecimal(8), r.GetString(9), r.GetBoolean(10));

    private static SalesInvoice MapInvoiceHeader(SqlDataReader r) => new(
        r.GetInt32(0), r.GetString(1), r.GetDateTime(2),
        r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
        r.IsDBNull(4) ? null : r.GetString(4),
        r.IsDBNull(5) ? null : r.GetString(5),
        r.GetInt32(6), r.GetString(7), r.GetDecimal(8), r.GetDecimal(9),
        r.GetDecimal(10), r.GetDecimal(11), r.GetDecimal(12), r.GetDecimal(13),
        r.GetString(14), r.GetString(15), r.IsDBNull(16) ? null : r.GetString(16),
        r.GetBoolean(17), r.IsDBNull(18) ? null : r.GetDateTime(18), r.GetDateTime(19),
        Array.Empty<SalesInvoiceItem>(), Array.Empty<SalesInvoicePayment>());

    private async Task<IReadOnlyList<SalesInvoiceItem>> GetItemsAsync(int invoiceId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT sii.Id, sii.InvoiceId, sii.ProductId, p.Barcode, p.NameAr,
                     sii.Quantity, sii.UnitPrice, sii.DiscountAmount, sii.LineTotal, sii.PriceTierUsed
              FROM dbo.SalesInvoiceItems sii
              LEFT JOIN dbo.Products p ON sii.ProductId = p.Id
              WHERE sii.InvoiceId=@InvoiceId", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@InvoiceId", SqlDbType.Int) { Value = invoiceId });
        var list = new List<SalesInvoiceItem>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new SalesInvoiceItem(
                r.GetInt32(0), r.GetInt32(1), r.GetInt32(2),
                r.IsDBNull(3) ? "" : r.GetString(3),
                r.IsDBNull(4) ? "" : r.GetString(4),
                r.GetInt32(5), r.GetDecimal(6), r.GetDecimal(7), r.GetDecimal(8), r.GetString(9)));
        return list;
    }

    private async Task<IReadOnlyList<SalesInvoicePayment>> GetPaymentsAsync(int invoiceId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT Id, InvoiceId, PaymentMethod, PaymentMethodAr, Amount, ReferenceNumber, PaidAt
              FROM dbo.SalesInvoicePayments WHERE InvoiceId=@InvoiceId", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@InvoiceId", SqlDbType.Int) { Value = invoiceId });
        var list = new List<SalesInvoicePayment>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new SalesInvoicePayment(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3),
                r.GetDecimal(4), r.IsDBNull(5) ? null : r.GetString(5), r.GetDateTime(6)));
        return list;
    }
}
