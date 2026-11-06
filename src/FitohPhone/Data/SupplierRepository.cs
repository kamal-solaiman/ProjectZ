using System.Data;
using FitohPhone.Models;
using FitohPhone.Services;
using Microsoft.Data.SqlClient;

namespace FitohPhone.Data;

public sealed class SupplierRepository(ConnectionSettingsProvider settings)
{
    public async Task<IReadOnlyList<Supplier>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT Id, Name, Phone, ContactPerson, Address, City, Email,
                     TotalPurchases, TotalPaid, OutstandingBalance,
                     IsActive, CreatedAt, UpdatedAt
              FROM dbo.Suppliers ORDER BY Name", conn) { CommandTimeout = 15 };
        var list = new List<Supplier>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(MapSupplier(r));
        return list;
    }

    public async Task<Supplier?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT Id, Name, Phone, ContactPerson, Address, City, Email,
                     TotalPurchases, TotalPaid, OutstandingBalance,
                     IsActive, CreatedAt, UpdatedAt
              FROM dbo.Suppliers WHERE Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapSupplier(r) : null;
    }

    public async Task<int> AddAsync(Supplier supplier, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"INSERT INTO dbo.Suppliers (Name, Phone, ContactPerson, Address, City, Email,
                     TotalPurchases, TotalPaid, OutstandingBalance, IsActive, CreatedAt, UpdatedAt)
              OUTPUT INSERTED.Id
              VALUES (@Name, @Phone, @ContactPerson, @Address, @City, @Email,
                     0, 0, 0, 1, SYSUTCDATETIME(), SYSUTCDATETIME())", conn) { CommandTimeout = 15 };
        cmd.Parameters.AddRange(new[]
        {
            new SqlParameter("@Name", SqlDbType.NVarChar, 200) { Value = supplier.Name },
            new SqlParameter("@Phone", SqlDbType.NVarChar, 20) { Value = supplier.Phone },
            new SqlParameter("@ContactPerson", SqlDbType.NVarChar, 200) { Value = (object)supplier.ContactPerson ?? DBNull.Value },
            new SqlParameter("@Address", SqlDbType.NVarChar, 500) { Value = (object)supplier.Address ?? DBNull.Value },
            new SqlParameter("@City", SqlDbType.NVarChar, 100) { Value = (object)supplier.City ?? DBNull.Value },
            new SqlParameter("@Email", SqlDbType.NVarChar, 200) { Value = (object)supplier.Email ?? DBNull.Value },
        });
        return (int)await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<bool> UpdateAsync(Supplier supplier, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"UPDATE dbo.Suppliers SET Name=@Name, Phone=@Phone, ContactPerson=@ContactPerson,
                     Address=@Address, City=@City, Email=@Email,
                     IsActive=@IsActive, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.AddRange(new[]
        {
            new SqlParameter("@Id", SqlDbType.Int) { Value = supplier.Id },
            new SqlParameter("@Name", SqlDbType.NVarChar, 200) { Value = supplier.Name },
            new SqlParameter("@Phone", SqlDbType.NVarChar, 20) { Value = supplier.Phone },
            new SqlParameter("@ContactPerson", SqlDbType.NVarChar, 200) { Value = (object)supplier.ContactPerson ?? DBNull.Value },
            new SqlParameter("@Address", SqlDbType.NVarChar, 500) { Value = (object)supplier.Address ?? DBNull.Value },
            new SqlParameter("@City", SqlDbType.NVarChar, 100) { Value = (object)supplier.City ?? DBNull.Value },
            new SqlParameter("@Email", SqlDbType.NVarChar, 200) { Value = (object)supplier.Email ?? DBNull.Value },
            new SqlParameter("@IsActive", SqlDbType.Bit) { Value = supplier.IsActive },
        });
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand("DELETE FROM dbo.Suppliers WHERE Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    private static Supplier MapSupplier(SqlDataReader r) => new(
        r.GetInt32(0), r.GetString(1), r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.IsDBNull(4) ? null : r.GetString(4),
        r.IsDBNull(5) ? null : r.GetString(5),
        r.IsDBNull(6) ? null : r.GetString(6),
        r.GetDecimal(7), r.GetDecimal(8), r.GetDecimal(9),
        r.GetBoolean(10), r.GetDateTime(11),
        r.IsDBNull(12) ? null : r.GetDateTime(12));
}
