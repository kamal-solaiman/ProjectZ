using System.Data;
using FitohPhone.Models;
using FitohPhone.Services;
using Microsoft.Data.SqlClient;

namespace FitohPhone.Data;

public sealed class BrandRepository(ConnectionSettingsProvider settings)
{
    public async Task<IReadOnlyList<ProductBrand>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT Id, Name, NameAr, Description, IsActive, CreatedAt FROM dbo.ProductBrands ORDER BY NameAr", conn) { CommandTimeout = 15 };
        var list = new List<ProductBrand>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new ProductBrand(
                r.GetInt32(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3), r.GetBoolean(4), r.GetDateTime(5)));
        return list;
    }

    public async Task<int> AddAsync(string name, string nameAr, string? description, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "INSERT INTO dbo.ProductBrands (Name, NameAr, Description, IsActive, CreatedAt) OUTPUT INSERTED.Id VALUES (@Name, @NameAr, @Description, 1, SYSUTCDATETIME())", conn) { CommandTimeout = 15 };
        cmd.Parameters.AddRange(new[]
        {
            new SqlParameter("@Name", SqlDbType.NVarChar, 100) { Value = name },
            new SqlParameter("@NameAr", SqlDbType.NVarChar, 100) { Value = nameAr },
            new SqlParameter("@Description", SqlDbType.NVarChar, 500) { Value = (object)description ?? DBNull.Value },
        });
        return (int)await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand("DELETE FROM dbo.ProductBrands WHERE Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }
}

public sealed record ProductBrand(int Id, string Name, string NameAr, string? Description, bool IsActive, DateTime CreatedAt);
