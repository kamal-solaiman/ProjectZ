using System.Data;
using FitohPhone.Models;
using FitohPhone.Services;
using Microsoft.Data.SqlClient;

namespace FitohPhone.Data;

public sealed class PhoneRepairRepository(ConnectionSettingsProvider settings)
{
    public async Task<IReadOnlyList<PhoneRepair>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT pr.Id, pr.RepairNumber, pr.CustomerId, c.Name, c.Phone,
                     pr.ReceivedByUserId, u1.Username, pr.DeviceModel, pr.DeviceIMEI,
                     pr.DeviceSerialNumber, pr.ProblemDescription, pr.Diagnosis, pr.Status,
                     pr.ReceivedDate, pr.EstimatedDeliveryDate, pr.ActualDeliveryDate,
                     pr.DeliveredByUserId, u2.Username, pr.RepairCost, pr.PartsCost,
                     pr.LaborCost, pr.TotalCost, pr.PaidAmount, pr.RemainingAmount,
                     pr.BarcodePrinted, pr.BarcodePrintedAt, pr.WarrantyPeriodDays,
                     pr.Notes, pr.CreatedAt, pr.UpdatedAt
              FROM dbo.PhoneRepairs pr
              LEFT JOIN dbo.Customers c ON pr.CustomerId = c.Id
              LEFT JOIN dbo.Users u1 ON pr.ReceivedByUserId = u1.Id
              LEFT JOIN dbo.Users u2 ON pr.DeliveredByUserId = u2.Id
              ORDER BY pr.ReceivedDate DESC", conn) { CommandTimeout = 15 };
        var list = new List<PhoneRepair>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(MapRepair(r));
        return list;
    }

    public async Task<PhoneRepair?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT pr.Id, pr.RepairNumber, pr.CustomerId, c.Name, c.Phone,
                     pr.ReceivedByUserId, u1.Username, pr.DeviceModel, pr.DeviceIMEI,
                     pr.DeviceSerialNumber, pr.ProblemDescription, pr.Diagnosis, pr.Status,
                     pr.ReceivedDate, pr.EstimatedDeliveryDate, pr.ActualDeliveryDate,
                     pr.DeliveredByUserId, u2.Username, pr.RepairCost, pr.PartsCost,
                     pr.LaborCost, pr.TotalCost, pr.PaidAmount, pr.RemainingAmount,
                     pr.BarcodePrinted, pr.BarcodePrintedAt, pr.WarrantyPeriodDays,
                     pr.Notes, pr.CreatedAt, pr.UpdatedAt
              FROM dbo.PhoneRepairs pr
              LEFT JOIN dbo.Customers c ON pr.CustomerId = c.Id
              LEFT JOIN dbo.Users u1 ON pr.ReceivedByUserId = u1.Id
              LEFT JOIN dbo.Users u2 ON pr.DeliveredByUserId = u2.Id
              WHERE pr.Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapRepair(r) : null;
    }

    public async Task<int> AddAsync(PhoneRepair repair, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            await using var cmd = new SqlCommand(
                @"INSERT INTO dbo.PhoneRepairs (RepairNumber, CustomerId, ReceivedByUserId,
                         DeviceModel, DeviceIMEI, DeviceSerialNumber, ProblemDescription,
                         Diagnosis, Status, ReceivedDate, EstimatedDeliveryDate,
                         RepairCost, PartsCost, LaborCost, TotalCost, PaidAmount,
                         RemainingAmount, BarcodePrinted, WarrantyPeriodDays, Notes,
                         CreatedAt, UpdatedAt)
              OUTPUT INSERTED.Id
              VALUES (@RepairNumber, @CustomerId, @ReceivedByUserId, @DeviceModel,
                     @DeviceIMEI, @DeviceSerialNumber, @ProblemDescription, @Diagnosis,
                     @Status, @ReceivedDate, @EstimatedDeliveryDate, @RepairCost,
                     @PartsCost, @LaborCost, @TotalCost, @PaidAmount, @RemainingAmount,
                     @BarcodePrinted, @WarrantyPeriodDays, @Notes, SYSUTCDATETIME(), SYSUTCDATETIME())",
                conn, tx) { CommandTimeout = 15 };
            cmd.Parameters.AddRange(new[]
            {
                new SqlParameter("@RepairNumber", SqlDbType.NVarChar, 50) { Value = repair.RepairNumber },
                new SqlParameter("@CustomerId", SqlDbType.Int) { Value = repair.CustomerId },
                new SqlParameter("@ReceivedByUserId", SqlDbType.Int) { Value = repair.ReceivedByUserId },
                new SqlParameter("@DeviceModel", SqlDbType.NVarChar, 200) { Value = repair.DeviceModel },
                new SqlParameter("@DeviceIMEI", SqlDbType.NVarChar, 50) { Value = (object)repair.DeviceIMEI ?? DBNull.Value },
                new SqlParameter("@DeviceSerialNumber", SqlDbType.NVarChar, 100) { Value = (object)repair.DeviceSerialNumber ?? DBNull.Value },
                new SqlParameter("@ProblemDescription", SqlDbType.NVarChar, 1000) { Value = repair.ProblemDescription },
                new SqlParameter("@Diagnosis", SqlDbType.NVarChar, 1000) { Value = (object)repair.Diagnosis ?? DBNull.Value },
                new SqlParameter("@Status", SqlDbType.NVarChar, 30) { Value = repair.Status },
                new SqlParameter("@ReceivedDate", SqlDbType.DateTime2) { Value = repair.ReceivedDate },
                new SqlParameter("@EstimatedDeliveryDate", SqlDbType.DateTime2) { Value = (object)repair.EstimatedDeliveryDate ?? DBNull.Value },
                new SqlParameter("@RepairCost", SqlDbType.Decimal) { Value = repair.RepairCost },
                new SqlParameter("@PartsCost", SqlDbType.Decimal) { Value = repair.PartsCost },
                new SqlParameter("@LaborCost", SqlDbType.Decimal) { Value = repair.LaborCost },
                new SqlParameter("@TotalCost", SqlDbType.Decimal) { Value = repair.TotalCost },
                new SqlParameter("@PaidAmount", SqlDbType.Decimal) { Value = repair.PaidAmount },
                new SqlParameter("@RemainingAmount", SqlDbType.Decimal) { Value = repair.RemainingAmount },
                new SqlParameter("@BarcodePrinted", SqlDbType.Bit) { Value = repair.BarcodePrinted },
                new SqlParameter("@WarrantyPeriodDays", SqlDbType.Int) { Value = (object)repair.WarrantyPeriodDays ?? DBNull.Value },
                new SqlParameter("@Notes", SqlDbType.NVarChar, 1000) { Value = (object)repair.Notes ?? DBNull.Value },
            });
            var id = (int)await cmd.ExecuteScalarAsync(ct);

            foreach (var item in repair.Items)
            {
                await using var icmd = new SqlCommand(
                    @"INSERT INTO dbo.RepairItems (RepairId, ItemType, ItemTypeAr, ItemName,
                             ItemCost, Quantity, IsCustomerSupplied, Notes)
                     VALUES (@RepairId, @ItemType, @ItemTypeAr, @ItemName, @ItemCost,
                     @Quantity, @IsCustomerSupplied, @Notes)", conn, tx) { CommandTimeout = 15 };
                icmd.Parameters.AddRange(new[]
                {
                    new SqlParameter("@RepairId", SqlDbType.Int) { Value = id },
                    new SqlParameter("@ItemType", SqlDbType.NVarChar, 30) { Value = item.ItemType },
                    new SqlParameter("@ItemTypeAr", SqlDbType.NVarChar, 50) { Value = item.ItemTypeAr },
                    new SqlParameter("@ItemName", SqlDbType.NVarChar, 200) { Value = item.ItemName },
                    new SqlParameter("@ItemCost", SqlDbType.Decimal) { Value = item.ItemCost },
                    new SqlParameter("@Quantity", SqlDbType.Int) { Value = item.Quantity },
                    new SqlParameter("@IsCustomerSupplied", SqlDbType.Bit) { Value = item.IsCustomerSupplied },
                    new SqlParameter("@Notes", SqlDbType.NVarChar, 500) { Value = (object)item.Notes ?? DBNull.Value },
                });
                await icmd.ExecuteNonQueryAsync(ct);
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

    public async Task<bool> UpdateStatusAsync(int id, string status, int? deliveredByUserId, DateTime? actualDeliveryDate, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"UPDATE dbo.PhoneRepairs SET Status=@Status,
                     DeliveredByUserId=@DeliveredBy, ActualDeliveryDate=@DeliveryDate,
                     UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        cmd.Parameters.Add(new SqlParameter("@Status", SqlDbType.NVarChar, 30) { Value = status });
        cmd.Parameters.Add(new SqlParameter("@DeliveredBy", SqlDbType.Int) { Value = (object)deliveredByUserId ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@DeliveryDate", SqlDbType.DateTime2) { Value = (object)actualDeliveryDate ?? DBNull.Value });
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> MarkBarcodePrintedAsync(int id, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"UPDATE dbo.PhoneRepairs SET BarcodePrinted=1, BarcodePrintedAt=SYSUTCDATETIME() WHERE Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    private static PhoneRepair MapRepair(SqlDataReader r) => new(
        r.GetInt32(0), r.GetString(1), r.GetInt32(2),
        r.IsDBNull(3) ? "" : r.GetString(3),
        r.IsDBNull(4) ? "" : r.GetString(4),
        r.GetInt32(5), r.GetString(6), r.GetString(7),
        r.IsDBNull(8) ? null : r.GetString(8),
        r.IsDBNull(9) ? null : r.GetString(9),
        r.GetString(10), r.IsDBNull(11) ? null : r.GetString(11), r.GetString(12),
        r.GetDateTime(13), r.IsDBNull(14) ? null : r.GetDateTime(14),
        r.IsDBNull(15) ? null : r.GetDateTime(15),
        r.IsDBNull(16) ? (int?)null : r.GetInt32(16),
        r.IsDBNull(17) ? null : r.GetString(17),
        r.GetDecimal(18), r.GetDecimal(19), r.GetDecimal(20), r.GetDecimal(21),
        r.GetDecimal(22), r.GetDecimal(23), r.GetBoolean(24),
        r.IsDBNull(25) ? null : r.GetDateTime(25),
        r.IsDBNull(26) ? (int?)null : r.GetInt32(26),
        r.IsDBNull(27) ? null : r.GetString(27),
        r.GetDateTime(28), r.IsDBNull(29) ? null : r.GetDateTime(29),
        Array.Empty<RepairItem>());
}
