using System.Data;
using FitohPhone.Models;
using FitohPhone.Services;
using Microsoft.Data.SqlClient;

namespace FitohPhone.Data;

public sealed class PurchaseInvoiceRepository(ConnectionSettingsProvider settings)
{
    public async Task<IReadOnlyList<PurchaseInvoiceListItem>> GetListAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT pi.Id, pi.InvoiceNumber, pi.InvoiceDate, s.Name, u.Username,
                     pi.GrandTotal, pi.PaidAmount, pi.RemainingAmount, pi.PaymentStatus, pi.IsCancelled
              FROM dbo.PurchaseInvoices pi
              LEFT JOIN dbo.Suppliers s ON pi.SupplierId = s.Id
              LEFT JOIN dbo.Users u ON pi.UserId = u.Id
              ORDER BY pi.InvoiceDate DESC", conn) { CommandTimeout = 15 };
        var list = new List<PurchaseInvoiceListItem>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new PurchaseInvoiceListItem(
                r.GetInt32(0), r.GetString(1), r.GetDateTime(2),
                r.GetString(3), r.GetString(4), r.GetDecimal(5),
                r.GetDecimal(6), r.GetDecimal(7), r.GetString(8), r.GetBoolean(9)));
        return list;
    }

    public async Task<PurchaseInvoice?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT pi.Id, pi.InvoiceNumber, pi.InvoiceDate, pi.SupplierId, s.Name,
                     pi.UserId, u.Username, pi.SubTotal, pi.GrandTotal, pi.PaidAmount,
                     pi.RemainingAmount, pi.PaymentStatus, pi.Notes, pi.IsCancelled,
                     pi.CancelledAt, pi.CreatedAt
              FROM dbo.PurchaseInvoices pi
              LEFT JOIN dbo.Suppliers s ON pi.SupplierId = s.Id
              LEFT JOIN dbo.Users u ON pi.UserId = u.Id
              WHERE pi.Id=@Id", conn) { CommandTimeout = 15 };
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

    public async Task<int> CreateAsync(PurchaseInvoice invoice, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            await using var cmd = new SqlCommand(
                @"INSERT INTO dbo.PurchaseInvoices (InvoiceNumber, InvoiceDate, SupplierId, UserId,
                         SubTotal, GrandTotal, PaidAmount, RemainingAmount, PaymentStatus,
                         Notes, IsCancelled, CreatedAt)
              OUTPUT INSERTED.Id
              VALUES (@InvoiceNumber, @InvoiceDate, @SupplierId, @UserId,
                         @SubTotal, @GrandTotal, @PaidAmount, @RemainingAmount, @PaymentStatus,
                         @Notes, 0, SYSUTCDATETIME())", conn, tx) { CommandTimeout = 15 };
            cmd.Parameters.AddRange(new[]
            {
                new SqlParameter("@InvoiceNumber", SqlDbType.NVarChar, 50) { Value = invoice.InvoiceNumber },
                new SqlParameter("@InvoiceDate", SqlDbType.DateTime2) { Value = invoice.InvoiceDate },
                new SqlParameter("@SupplierId", SqlDbType.Int) { Value = invoice.SupplierId },
                new SqlParameter("@UserId", SqlDbType.Int) { Value = invoice.UserId },
                new SqlParameter("@SubTotal", SqlDbType.Decimal) { Value = invoice.SubTotal },
                new SqlParameter("@GrandTotal", SqlDbType.Decimal) { Value = invoice.GrandTotal },
                new SqlParameter("@PaidAmount", SqlDbType.Decimal) { Value = invoice.PaidAmount },
                new SqlParameter("@RemainingAmount", SqlDbType.Decimal) { Value = invoice.RemainingAmount },
                new SqlParameter("@PaymentStatus", SqlDbType.NVarChar, 20) { Value = invoice.PaymentStatus },
                new SqlParameter("@Notes", SqlDbType.NVarChar, 1000) { Value = (object)invoice.Notes ?? DBNull.Value },
            });
            var id = (int)await cmd.ExecuteScalarAsync(ct);

            foreach (var item in invoice.Items)
            {
                await using var icmd = new SqlCommand(
                    @"INSERT INTO dbo.PurchaseInvoiceItems (InvoiceId, ProductId, Quantity, UnitCost, LineTotal)
                     VALUES (@InvoiceId, @ProductId, @Quantity, @UnitCost, @LineTotal)", conn, tx) { CommandTimeout = 15 };
                icmd.Parameters.AddRange(new[]
                {
                    new SqlParameter("@InvoiceId", SqlDbType.Int) { Value = id },
                    new SqlParameter("@ProductId", SqlDbType.Int) { Value = item.ProductId },
                    new SqlParameter("@Quantity", SqlDbType.Int) { Value = item.Quantity },
                    new SqlParameter("@UnitCost", SqlDbType.Decimal) { Value = item.UnitCost },
                    new SqlParameter("@LineTotal", SqlDbType.Decimal) { Value = item.LineTotal },
                });
                await icmd.ExecuteNonQueryAsync(ct);

                // تحديث رصيد المنتج في المخزون
                await using var scmd = new SqlCommand(
                    "UPDATE dbo.Products SET CurrentStock = CurrentStock + @Qty, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id", conn, tx) { CommandTimeout = 15 };
                scmd.Parameters.Add(new SqlParameter("@Qty", SqlDbType.Int) { Value = item.Quantity });
                scmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = item.ProductId });
                await scmd.ExecuteNonQueryAsync(ct);
            }

            foreach (var pay in invoice.Payments)
            {
                await using var pcmd = new SqlCommand(
                    @"INSERT INTO dbo.PurchaseInvoicePayments (InvoiceId, PaymentMethod, PaymentMethodAr, Amount, PaidAt)
                     VALUES (@InvoiceId, @PaymentMethod, @PaymentMethodAr, @Amount, @PaidAt)", conn, tx) { CommandTimeout = 15 };
                pcmd.Parameters.AddRange(new[]
                {
                    new SqlParameter("@InvoiceId", SqlDbType.Int) { Value = id },
                    new SqlParameter("@PaymentMethod", SqlDbType.NVarChar, 50) { Value = pay.PaymentMethod },
                    new SqlParameter("@PaymentMethodAr", SqlDbType.NVarChar, 50) { Value = pay.PaymentMethodAr },
                    new SqlParameter("@Amount", SqlDbType.Decimal) { Value = pay.Amount },
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
            @"UPDATE dbo.PurchaseInvoices SET IsCancelled=1, CancelledAt=SYSUTCDATETIME(), CancelledByUserId=@CancelledBy
              WHERE Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = invoiceId });
        cmd.Parameters.Add(new SqlParameter("@CancelledBy", SqlDbType.Int) { Value = (object)cancelledByUserId ?? DBNull.Value });
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    private static PurchaseInvoiceListItem MapListItem(SqlDataReader r) => new(
        r.GetInt32(0), r.GetString(1), r.GetDateTime(2),
        r.GetString(3), r.GetString(4), r.GetDecimal(5),
        r.GetDecimal(6), r.GetDecimal(7), r.GetString(8), r.GetBoolean(9));

    private static PurchaseInvoice MapInvoiceHeader(SqlDataReader r) => new(
        r.GetInt32(0), r.GetString(1), r.GetDateTime(2),
        r.GetInt32(3), r.GetString(4), r.GetInt32(5), r.GetString(6),
        r.GetDecimal(7), r.GetDecimal(8), r.GetDecimal(9), r.GetDecimal(10),
        r.GetString(11), r.IsDBNull(12) ? null : r.GetString(12),
        r.GetBoolean(13), r.IsDBNull(14) ? null : r.GetDateTime(14), r.GetDateTime(15),
        Array.Empty<PurchaseInvoiceItem>(), Array.Empty<PurchaseInvoicePayment>());

    private async Task<IReadOnlyList<PurchaseInvoiceItem>> GetItemsAsync(int invoiceId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT pii.Id, pii.InvoiceId, pii.ProductId, p.Barcode, p.NameAr,
                     pii.Quantity, pii.UnitCost, pii.LineTotal
              FROM dbo.PurchaseInvoiceItems pii
              LEFT JOIN dbo.Products p ON pii.ProductId = p.Id
              WHERE pii.InvoiceId=@InvoiceId", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@InvoiceId", SqlDbType.Int) { Value = invoiceId });
        var list = new List<PurchaseInvoiceItem>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new PurchaseInvoiceItem(
                r.GetInt32(0), r.GetInt32(1), r.GetInt32(2),
                r.IsDBNull(3) ? "" : r.GetString(3),
                r.IsDBNull(4) ? "" : r.GetString(4),
                r.GetInt32(5), r.GetDecimal(6), r.GetDecimal(7)));
        return list;
    }

    private async Task<IReadOnlyList<PurchaseInvoicePayment>> GetPaymentsAsync(int invoiceId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT Id, InvoiceId, PaymentMethod, PaymentMethodAr, Amount, PaidAt
              FROM dbo.PurchaseInvoicePayments WHERE InvoiceId=@InvoiceId", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@InvoiceId", SqlDbType.Int) { Value = invoiceId });
        var list = new List<PurchaseInvoicePayment>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new PurchaseInvoicePayment(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3),
                r.GetDecimal(4), r.GetDateTime(5)));
        return list;
    }
}
