using System.Data;
using FitohPhone.Models;
using FitohPhone.Services;
using Microsoft.Data.SqlClient;

namespace FitohPhone.Data;

public sealed class ProductRepository(ConnectionSettingsProvider settings)
{
    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT p.Id, p.Barcode, p.NameAr, p.NameEn, p.CategoryId, c.Name AS CategoryName,
                     p.BrandId, b.Name AS BrandName, p.CostPrice, p.PriceWholesale,
                     p.PriceSpecial, p.PriceRetail, p.MinStock, p.CurrentStock,
                     p.Unit, p.WarrantyMonths, p.IsActive, p.CreatedAt, p.UpdatedAt
              FROM dbo.Products p
              LEFT JOIN dbo.ProductCategories c ON p.CategoryId = c.Id
              LEFT JOIN dbo.ProductBrands b ON p.BrandId = b.Id
              ORDER BY p.NameAr", conn) { CommandTimeout = 15 };
        var list = new List<Product>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(MapProduct(r));
        return list;
    }

    public async Task<Product?> GetByIdAsync(int id, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT p.Id, p.Barcode, p.NameAr, p.NameEn, p.CategoryId, c.Name AS CategoryName,
                     p.BrandId, b.Name AS BrandName, p.CostPrice, p.PriceWholesale,
                     p.PriceSpecial, p.PriceRetail, p.MinStock, p.CurrentStock,
                     p.Unit, p.WarrantyMonths, p.IsActive, p.CreatedAt, p.UpdatedAt
              FROM dbo.Products p
              LEFT JOIN dbo.ProductCategories c ON p.CategoryId = c.Id
              LEFT JOIN dbo.ProductBrands b ON p.BrandId = b.Id
              WHERE p.Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapProduct(r) : null;
    }

    public async Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT p.Id, p.Barcode, p.NameAr, p.NameEn, p.CategoryId, c.Name AS CategoryName,
                     p.BrandId, b.Name AS BrandName, p.CostPrice, p.PriceWholesale,
                     p.PriceSpecial, p.PriceRetail, p.MinStock, p.CurrentStock,
                     p.Unit, p.WarrantyMonths, p.IsActive, p.CreatedAt, p.UpdatedAt
              FROM dbo.Products p
              LEFT JOIN dbo.ProductCategories c ON p.CategoryId = c.Id
              LEFT JOIN dbo.ProductBrands b ON p.BrandId = b.Id
              WHERE p.Barcode=@Barcode", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@Barcode", SqlDbType.NVarChar, 100) { Value = barcode });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? MapProduct(r) : null;
    }

    public async Task<int> AddAsync(Product product, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"INSERT INTO dbo.Products (Barcode, NameAr, NameEn, CategoryId, BrandId,
                     CostPrice, PriceWholesale, PriceSpecial, PriceRetail, MinStock,
                     CurrentStock, Unit, WarrantyMonths, IsActive, CreatedAt, UpdatedAt)
              OUTPUT INSERTED.Id
              VALUES (@Barcode, @NameAr, @NameEn, @CategoryId, @BrandId,
                     @CostPrice, @PriceWholesale, @PriceSpecial, @PriceRetail, @MinStock,
                     @CurrentStock, @Unit, @WarrantyMonths, 1, SYSUTCDATETIME(), SYSUTCDATETIME())", conn) { CommandTimeout = 15 };
        cmd.Parameters.AddRange(new[]
        {
            new SqlParameter("@Barcode", SqlDbType.NVarChar, 100) { Value = product.Barcode },
            new SqlParameter("@NameAr", SqlDbType.NVarChar, 200) { Value = product.NameAr },
            new SqlParameter("@NameEn", SqlDbType.NVarChar, 200) { Value = (object)product.NameEn ?? DBNull.Value },
            new SqlParameter("@CategoryId", SqlDbType.Int) { Value = (object)product.CategoryId ?? DBNull.Value },
            new SqlParameter("@BrandId", SqlDbType.Int) { Value = (object)product.BrandId ?? DBNull.Value },
            new SqlParameter("@CostPrice", SqlDbType.Decimal) { Value = product.CostPrice },
            new SqlParameter("@PriceWholesale", SqlDbType.Decimal) { Value = product.PriceWholesale },
            new SqlParameter("@PriceSpecial", SqlDbType.Decimal) { Value = product.PriceSpecial },
            new SqlParameter("@PriceRetail", SqlDbType.Decimal) { Value = product.PriceRetail },
            new SqlParameter("@MinStock", SqlDbType.Int) { Value = product.MinStock },
            new SqlParameter("@CurrentStock", SqlDbType.Int) { Value = product.CurrentStock },
            new SqlParameter("@Unit", SqlDbType.NVarChar, 20) { Value = product.Unit },
            new SqlParameter("@WarrantyMonths", SqlDbType.Int) { Value = (object)product.WarrantyMonths ?? DBNull.Value },
        });
        return (int)await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<bool> UpdateAsync(Product product, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"UPDATE dbo.Products SET Barcode=@Barcode, NameAr=@NameAr, NameEn=@NameEn,
                     CategoryId=@CategoryId, BrandId=@BrandId, CostPrice=@CostPrice,
                     PriceWholesale=@PriceWholesale, PriceSpecial=@PriceSpecial,
                     PriceRetail=@PriceRetail, MinStock=@MinStock,
                     CurrentStock=@CurrentStock, Unit=@Unit, WarrantyMonths=@WarrantyMonths,
                     IsActive=@IsActive, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.AddRange(new[]
        {
            new SqlParameter("@Id", SqlDbType.Int) { Value = product.Id },
            new SqlParameter("@Barcode", SqlDbType.NVarChar, 100) { Value = product.Barcode },
            new SqlParameter("@NameAr", SqlDbType.NVarChar, 200) { Value = product.NameAr },
            new SqlParameter("@NameEn", SqlDbType.NVarChar, 200) { Value = (object)product.NameEn ?? DBNull.Value },
            new SqlParameter("@CategoryId", SqlDbType.Int) { Value = (object)product.CategoryId ?? DBNull.Value },
            new SqlParameter("@BrandId", SqlDbType.Int) { Value = (object)product.BrandId ?? DBNull.Value },
            new SqlParameter("@CostPrice", SqlDbType.Decimal) { Value = product.CostPrice },
            new SqlParameter("@PriceWholesale", SqlDbType.Decimal) { Value = product.PriceWholesale },
            new SqlParameter("@PriceSpecial", SqlDbType.Decimal) { Value = product.PriceSpecial },
            new SqlParameter("@PriceRetail", SqlDbType.Decimal) { Value = product.PriceRetail },
            new SqlParameter("@MinStock", SqlDbType.Int) { Value = product.MinStock },
            new SqlParameter("@CurrentStock", SqlDbType.Int) { Value = product.CurrentStock },
            new SqlParameter("@Unit", SqlDbType.NVarChar, 20) { Value = product.Unit },
            new SqlParameter("@WarrantyMonths", SqlDbType.Int) { Value = (object)product.WarrantyMonths ?? DBNull.Value },
            new SqlParameter("@IsActive", SqlDbType.Bit) { Value = product.IsActive },
        });
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand("DELETE FROM dbo.Products WHERE Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> UpdateStockAsync(int productId, int quantityChange, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"UPDATE dbo.Products SET CurrentStock = CurrentStock + @Qty, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@Qty", SqlDbType.Int) { Value = quantityChange });
        cmd.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = productId });
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<IReadOnlyList<ProductStockItem>> GetLowStockAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT p.Id, p.Barcode, p.NameAr, ISNULL(c.Name,N'غير محدد') AS CategoryName,
                     ISNULL(b.Name,N'غير محدد') AS BrandName, p.CostPrice, p.PriceRetail,
                     p.CurrentStock, p.MinStock,
                     CASE WHEN p.CurrentStock <= p.MinStock THEN 1 ELSE 0 END AS IsLowStock
              FROM dbo.Products p
              LEFT JOIN dbo.ProductCategories c ON p.CategoryId = c.Id
              LEFT JOIN dbo.ProductBrands b ON p.BrandId = b.Id
              WHERE p.CurrentStock <= p.MinStock AND p.IsActive=1
              ORDER BY p.NameAr", conn) { CommandTimeout = 15 };
        var list = new List<ProductStockItem>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new ProductStockItem(
                r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.GetDecimal(5), r.GetDecimal(6), r.GetInt32(7), r.GetInt32(8),
                r.GetBoolean(9)));
        return list;
    }

    private static Product MapProduct(SqlDataReader r) => new(
        r.GetInt32(0), r.GetString(1), r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.IsDBNull(4) ? (int?)null : r.GetInt32(4),
        r.IsDBNull(5) ? null : r.GetString(5),
        r.IsDBNull(6) ? (int?)null : r.GetInt32(6),
        r.IsDBNull(7) ? null : r.GetString(7),
        r.GetDecimal(8), r.GetDecimal(9), r.GetDecimal(10), r.GetDecimal(11),
        r.GetInt32(12), r.GetInt32(13), r.GetString(14),
        r.IsDBNull(15) ? (int?)null : r.GetInt32(15),
        r.GetBoolean(16), r.GetDateTime(17),
        r.IsDBNull(18) ? null : r.GetDateTime(18));
}
