using System.Collections.ObjectModel;
using System.Data;
using Microsoft.Data.SqlClient;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Data;

/// <summary>User-facing failure raised by the maintenance layer; the message is safe to show in the UI.</summary>
public sealed class MaintenanceDataException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

/// <summary>Result of saving a spare part (add or update).</summary>
public sealed record SavedSparePart(
    int Id,
    string Name,
    string Category,
    int Quantity,
    int MinimumStockAlert,
    decimal CostPrice,
    decimal SellPrice);

/// <summary>SQL Server persistence for the maintenance spare-parts inventory and order-part lines.</summary>
public sealed class MaintenanceRepository(ConnectionSettingsProvider settings, int actorUserId)
{
    // ---------------------------------------------------------------- spare parts

    /// <summary>Loads the active spare parts (newest first) into the given collection.</summary>
    public void LoadSparePartsInto(ObservableCollection<MaintenanceSparePartItem> target)
    {
        var loaded = new List<MaintenanceSparePartItem>();
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT Id, Name, ISNULL(Category,N''), Quantity, MinimumStockAlert, CostPrice, SellPrice
FROM dbo.MaintenanceSpareParts
WHERE IsActive = 1
ORDER BY Id DESC;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                loaded.Add(new MaintenanceSparePartItem
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Category = reader.GetString(2),
                    Quantity = reader.GetInt32(3),
                    MinimumStockAlert = reader.GetInt32(4),
                    CostPrice = reader.GetDecimal(5),
                    SellPrice = reader.GetDecimal(6)
                });
            }
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            throw Describe(ex, "تعذّر تحميل مخزون قطع غيار الصيانة من قاعدة البيانات.");
        }

        target.Clear();
        foreach (var item in loaded) target.Add(item);
    }

    /// <summary>Adds a new spare part or updates an existing one; returns the persisted row.</summary>
    public SavedSparePart SaveSparePart(
        int? existingPartId,
        string name,
        string category,
        int quantity,
        int minimumStockAlert,
        decimal costPrice,
        decimal sellPrice)
    {
        name = Trim(name, 250, "");
        category = Trim(category, 100, "شاشات");
        quantity = Math.Max(0, quantity);
        minimumStockAlert = Math.Max(0, minimumStockAlert);
        costPrice = Math.Max(0m, costPrice);
        sellPrice = Math.Max(0m, sellPrice);

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            if (existingPartId is null)
            {
                int partId;
                using (var insert = CreateCommand(connection, transaction, @"
INSERT dbo.MaintenanceSpareParts(Name, Category, Quantity, MinimumStockAlert, CostPrice, SellPrice, IsActive)
OUTPUT inserted.Id
VALUES(@Name,@Category,@Qty,@Min,@Cost,@Sell,1);"))
                {
                    insert.Parameters.Add("@Name", SqlDbType.NVarChar, 250).Value = name;
                    insert.Parameters.Add("@Category", SqlDbType.NVarChar, 100).Value = category;
                    insert.Parameters.Add("@Qty", SqlDbType.Int).Value = quantity;
                    insert.Parameters.Add("@Min", SqlDbType.Int).Value = minimumStockAlert;
                    AddMoney(insert, "@Cost", costPrice);
                    AddMoney(insert, "@Sell", sellPrice);
                    partId = (int)insert.ExecuteScalar()!;
                }
                transaction.Commit();
                return new SavedSparePart(partId, name, category, quantity, minimumStockAlert, costPrice, sellPrice);
            }

            using (var update = CreateCommand(connection, transaction, @"
UPDATE dbo.MaintenanceSpareParts SET Name = @Name, Category = @Category, Quantity = @Qty,
     MinimumStockAlert = @Min, CostPrice = @Cost, SellPrice = @Sell, UpdatedAtUtc = SYSUTCDATETIME()
WHERE Id = @Id AND IsActive = 1;"))
            {
                update.Parameters.Add("@Name", SqlDbType.NVarChar, 250).Value = name;
                update.Parameters.Add("@Category", SqlDbType.NVarChar, 100).Value = category;
                update.Parameters.Add("@Qty", SqlDbType.Int).Value = quantity;
                update.Parameters.Add("@Min", SqlDbType.Int).Value = minimumStockAlert;
                AddMoney(update, "@Cost", costPrice);
                AddMoney(update, "@Sell", sellPrice);
                update.Parameters.Add("@Id", SqlDbType.Int).Value = existingPartId.Value;
                if (update.ExecuteNonQuery() == 0)
                    throw new MaintenanceDataException("تعذّر تعديل قطعة الغيار: غير موجودة أو متوقفة.");
            }

            transaction.Commit();
            return new SavedSparePart(existingPartId.Value, name, category, quantity, minimumStockAlert, costPrice, sellPrice);
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر حفظ قطعة الغيار.");
        }
    }

    /// <summary>
    /// Deletes a spare part. A part that has been used in an order-part line is deactivated
    /// (IsActive = 0) instead of being removed, to preserve the audit trail; an unused part is
    /// deleted permanently.
    /// </summary>
    public (bool Deleted, string Message) DeleteSparePart(int partId, string name)
    {
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            int usageCount;
            using (var count = CreateCommand(connection, transaction, @"
SELECT COUNT(1) FROM dbo.MaintenanceOrderParts WHERE SparePartId = @Id;"))
            {
                count.Parameters.Add("@Id", SqlDbType.Int).Value = partId;
                usageCount = (int)count.ExecuteScalar()!;
            }

            if (usageCount == 0)
            {
                using (var delete = CreateCommand(connection, transaction, @"
DELETE FROM dbo.MaintenanceSpareParts WHERE Id = @Id AND IsActive = 1;"))
                {
                    delete.Parameters.Add("@Id", SqlDbType.Int).Value = partId;
                    if (delete.ExecuteNonQuery() == 0)
                        throw new MaintenanceDataException("تعذّر حذف قطعة الغيار: غير موجودة أو متوقفة.");
                }
                transaction.Commit();
                return (true, $"تم حذف قطعة الغيار «{name}» نهائيًا.");
            }

            using (var deactivate = CreateCommand(connection, transaction, @"
UPDATE dbo.MaintenanceSpareParts SET IsActive = 0, UpdatedAtUtc = SYSUTCDATETIME()
WHERE Id = @Id AND IsActive = 1;"))
            {
                deactivate.Parameters.Add("@Id", SqlDbType.Int).Value = partId;
                deactivate.ExecuteNonQuery();
            }
            transaction.Commit();
            return (false, $"يوجد سجل صرف لـ «{name}»، تم إيقافه مؤقتًا بدل الحذف النهائي.");
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر حذف قطعة الغيار.");
        }
    }

    /// <summary>
    /// Deducts stock from a spare part and records the order-part line. In one transaction it:
    /// validates the available stock, updates the spare-part quantity (optimistic concurrency via
    /// RowVersion), and inserts a MaintenanceOrderParts row referencing the spare part. When orderId
    /// is 0 the part stock is still deducted but no order-part line is written.
    /// </summary>
    public void DeductSparePartStock(
        int sparePartId,
        int quantity,
        int orderId,
        string productName,
        string barcode,
        decimal unitCost,
        decimal unitSellPrice,
        int lineNumber)
    {
        quantity = Math.Max(1, quantity);
        productName = Trim(productName, 250, "");
        barcode = Trim(barcode, 100, "");
        unitCost = Math.Max(0m, unitCost);
        unitSellPrice = Math.Max(0m, unitSellPrice);

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            int currentQty;
            byte[] rowVersion;
            using (var lockRow = CreateCommand(connection, transaction, @"
SELECT Quantity, RowVersion FROM dbo.MaintenanceSpareParts WITH (UPDLOCK, HOLDLOCK)
WHERE Id = @Id AND IsActive = 1;"))
            {
                lockRow.Parameters.Add("@Id", SqlDbType.Int).Value = sparePartId;
                using var reader = lockRow.ExecuteReader();
                if (!reader.Read())
                    throw new MaintenanceDataException("تعذّر صرف القطعة: قطعة الغيار غير موجودة أو متوقفة.");
                currentQty = reader.GetInt32(0);
                rowVersion = (byte[])reader.GetValue(1);
            }

            if (currentQty < quantity)
                throw new MaintenanceDataException($"رصيد قطعة الغيار لا يكفي للصرف (المتاح: {currentQty}).");

            int newQty;
            using (var update = CreateCommand(connection, transaction, @"
UPDATE dbo.MaintenanceSpareParts SET Quantity = Quantity - @Qty, UpdatedAtUtc = SYSUTCDATETIME()
OUTPUT inserted.Quantity
WHERE Id = @Id AND IsActive = 1 AND RowVersion = @RV;"))
            {
                update.Parameters.Add("@Qty", SqlDbType.Int).Value = quantity;
                update.Parameters.Add("@Id", SqlDbType.Int).Value = sparePartId;
                update.Parameters.Add("@RV", SqlDbType.VarBinary).Value = rowVersion;
                newQty = (int)update.ExecuteScalar()!;
            }

            if (orderId > 0)
            {
                using (var insert = CreateCommand(connection, transaction, @"
INSERT dbo.MaintenanceOrderParts(MaintenanceOrderId, SparePartId, ProductId, LineNumber, BarcodeSnapshot,
 ProductNameSnapshot, Quantity, UnitCost, UnitSellPrice, IsManualPart)
VALUES(@Order,@Spare,NULL,@Line,@Barcode,@Name,@Qty,@Cost,@Sell,0);"))
                {
                    insert.Parameters.Add("@Order", SqlDbType.Int).Value = orderId;
                    insert.Parameters.Add("@Spare", SqlDbType.Int).Value = sparePartId;
                    insert.Parameters.Add("@Line", SqlDbType.Int).Value = lineNumber;
                    insert.Parameters.Add("@Barcode", SqlDbType.NVarChar, 100).Value = barcode;
                    insert.Parameters.Add("@Name", SqlDbType.NVarChar, 250).Value = productName;
                    insert.Parameters.Add("@Qty", SqlDbType.Int).Value = quantity;
                    AddMoney(insert, "@Cost", unitCost);
                    AddMoney(insert, "@Sell", unitSellPrice);
                    insert.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر صرف قطعة الغيار.");
        }
    }

    /// <summary>Restores stock back to a spare part after an order-part line is removed or reversed.</summary>
    public void RestoreSparePartStock(int sparePartId, int quantity)
    {
        quantity = Math.Max(1, quantity);
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            using (var update = CreateCommand(connection, transaction, @"
UPDATE dbo.MaintenanceSpareParts SET Quantity = Quantity + @Qty, UpdatedAtUtc = SYSUTCDATETIME()
WHERE Id = @Id AND IsActive = 1;"))
            {
                update.Parameters.Add("@Qty", SqlDbType.Int).Value = quantity;
                update.Parameters.Add("@Id", SqlDbType.Int).Value = sparePartId;
                update.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر استرجاع رصيد قطعة الغيار.");
        }
    }

    // ---------------------------------------------------------------- helpers

    private static SqlCommand CreateCommand(SqlConnection connection, SqlTransaction transaction, string sql) =>
        new(sql, connection, transaction) { CommandTimeout = 30 };

    private static void AddMoney(SqlCommand command, string name, decimal value)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = 19;
        parameter.Scale = 2;
        parameter.Value = value;
    }

    private static MaintenanceDataException Describe(Exception exception, string fallback)
    {
        if (exception is MaintenanceDataException data) return data;
        if (exception is SqlException sql)
        {
            return sql.Number switch
            {
                2601 or 2627 => new MaintenanceDataException("البيانات مكررة: قطعة الغيار مسجلة بالفعل. أعد المحاولة.", sql),
                208 => new MaintenanceDataException("جدول قطع الغيار غير موجود. شغّل sql/06_CreateFullSchema.sql على الخادم ثم أعد المحاولة.", sql),
                53 or 4060 => new MaintenanceDataException("لا يمكن الاتصال بقاعدة البيانات. تحقق من اتصال الخادم وتحديثه.", sql),
                18456 => new MaintenanceDataException("فشل تسجيل الدخول إلى قاعدة البيانات. تحقق من المستخدم وكلمة المرور.", sql),
                547 => new MaintenanceDataException("لا يمكن إتمام العملية لارتباط السجل بسجل صرف آخر.", sql),
                1205 => new MaintenanceDataException("تعارضت العملية مع مستخدم آخر. حدّث البيانات ثم أعد المحاولة.", sql),
                229 or 230 => new MaintenanceDataException(
                    "حساب الاتصال لا يملك صلاحية العمل على قطع غيار الصيانة. شغّل sql/09_EnablePosSales.sql على الخادم ثم أعد المحاولة.", sql),
                >= 50000 => new MaintenanceDataException(sql.Message, sql),
                _ => new MaintenanceDataException(fallback, sql)
            };
        }

        return new MaintenanceDataException(fallback, exception);
    }

    private static string Trim(string? value, int max, string fallback)
    {
        var trimmed = (value ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed)) return fallback;
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private SqlConnection OpenConnection()
    {
        var connection = new SqlConnection(settings.GetConnectionString());
        connection.Open();
        return connection;
    }
}
