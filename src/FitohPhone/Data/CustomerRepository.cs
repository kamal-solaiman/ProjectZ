using System.Data;
using FitohPhone.Models;
using FitohPhone.Services;
using Microsoft.Data.SqlClient;

namespace FitohPhone.Data;

public sealed class CustomerRepository(ConnectionSettingsProvider settings)
{
    public async Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT Id, Name, Phone, AlternatePhone, Address, City, Email,
                     TotalPurchases, TotalReturns, OutstandingBalance, LastVisit,
                     IsActive, CreatedAt, UpdatedAt
              FROM dbo.Customers ORDER BY Name", conn) { CommandTimeout = 15 };
        var list = new List<Customer>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(MapCustomer(r));
        return list;
    }

    public async Task<Customer?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT Id, Name, Phone, AlternatePhone, Address, City, Email,
                     TotalPurchases, TotalReturns, OutstandingBalance, LastVisit,
                     IsActive, CreatedAt, UpdatedAt
              FROM dbo.Customers WHERE Id = @Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapCustomer(r) : null;
    }

    public async Task<Customer?> GetByPhoneAsync(string phone, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT Id, Name, Phone, AlternatePhone, Address, City, Email,
                     TotalPurchases, TotalReturns, OutstandingBalance, LastVisit,
                     IsActive, CreatedAt, UpdatedAt
              FROM dbo.Customers WHERE Phone = @Phone", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@Phone", SqlDbType.NVarChar, 20) { Value = phone });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapCustomer(r) : null;
    }

    public async Task<int> AddAsync(Customer customer, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"INSERT INTO dbo.Customers (Name, Phone, AlternatePhone, Address, City, Email,
                     TotalPurchases, TotalReturns, OutstandingBalance, LastVisit, IsActive, CreatedAt, UpdatedAt)
              OUTPUT INSERTED.Id
              VALUES (@Name, @Phone, @AltPhone, @Address, @City, @Email,
                     0, 0, 0, NULL, 1, SYSUTCDATETIME(), SYSUTCDATETIME())", conn) { CommandTimeout = 15 };
        cmd.Parameters.AddRange(new[]
        {
            new SqlParameter("@Name", SqlDbType.NVarChar, 200) { Value = customer.Name },
            new SqlParameter("@Phone", SqlDbType.NVarChar, 20) { Value = customer.Phone },
            new SqlParameter("@AltPhone", SqlDbType.NVarChar, 20) { Value = (object)customer.AlternatePhone ?? DBNull.Value },
            new SqlParameter("@Address", SqlDbType.NVarChar, 500) { Value = (object)customer.Address ?? DBNull.Value },
            new SqlParameter("@City", SqlDbType.NVarChar, 100) { Value = (object)customer.City ?? DBNull.Value },
            new SqlParameter("@Email", SqlDbType.NVarChar, 200) { Value = (object)customer.Email ?? DBNull.Value },
        });
        return (int)await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<bool> UpdateAsync(Customer customer, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"UPDATE dbo.Customers SET Name=@Name, Phone=@Phone, AlternatePhone=@AltPhone,
                     Address=@Address, City=@City, Email=@Email,
                     IsActive=@IsActive, UpdatedAt=SYSUTCDATETIME()
              WHERE Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.AddRange(new[]
        {
            new SqlParameter("@Id", SqlDbType.Int) { Value = customer.Id },
            new SqlParameter("@Name", SqlDbType.NVarChar, 200) { Value = customer.Name },
            new SqlParameter("@Phone", SqlDbType.NVarChar, 20) { Value = customer.Phone },
            new SqlParameter("@AltPhone", SqlDbType.NVarChar, 20) { Value = (object)customer.AlternatePhone ?? DBNull.Value },
            new SqlParameter("@Address", SqlDbType.NVarChar, 500) { Value = (object)customer.Address ?? DBNull.Value },
            new SqlParameter("@City", SqlDbType.NVarChar, 100) { Value = (object)customer.City ?? DBNull.Value },
            new SqlParameter("@Email", SqlDbType.NVarChar, 200) { Value = (object)customer.Email ?? DBNull.Value },
            new SqlParameter("@IsActive", SqlDbType.Bit) { Value = customer.IsActive },
        });
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand("DELETE FROM dbo.Customers WHERE Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<IReadOnlyList<CustomerSummary>> GetSummariesAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT c.Id, c.Name, c.Phone, c.TotalPurchases, c.TotalReturns, c.OutstandingBalance,
                     COUNT(DISTINCT si.Id) AS InvoiceCount,
                     COUNT(DISTINCT pr.Id) AS RepairCount,
                     COUNT(DISTINCT re.Id) AS ReturnCount
              FROM dbo.Customers c
              LEFT JOIN dbo.SalesInvoices si ON si.CustomerId = c.Id
              LEFT JOIN dbo.PhoneRepairs pr ON pr.CustomerId = c.Id
              LEFT JOIN dbo.ReturnsExchanges re ON re.CustomerId = c.Id
              GROUP BY c.Id, c.Name, c.Phone, c.TotalPurchases, c.TotalReturns, c.OutstandingBalance
              ORDER BY c.Name", conn) { CommandTimeout = 15 };
        var list = new List<CustomerSummary>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new CustomerSummary(
                r.GetInt32(0), r.GetString(1), r.GetString(2),
                r.GetDecimal(3), r.GetDecimal(4), r.GetDecimal(5),
                r.GetInt32(6), r.GetInt32(7), r.GetInt32(8)));
        return list;
    }

    private static Customer MapCustomer(SqlDataReader r) => new(
        r.GetInt32(0), r.GetString(1), r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.IsDBNull(4) ? null : r.GetString(4),
        r.IsDBNull(5) ? null : r.GetString(5),
        r.IsDBNull(6) ? null : r.GetString(6),
        r.GetDecimal(7), r.GetDecimal(8), r.GetDecimal(9),
        r.IsDBNull(10) ? null : r.GetDateTime(10),
        r.GetBoolean(11), r.GetDateTime(12),
        r.IsDBNull(13) ? null : r.GetDateTime(13));
}
