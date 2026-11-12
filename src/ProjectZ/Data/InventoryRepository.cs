using System.Collections.ObjectModel;
using System.Data;
using Microsoft.Data.SqlClient;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Data;

public sealed class InventoryDataException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

/// <summary>SQL Server persistence for the inventory screen only.</summary>
public sealed class InventoryRepository(ConnectionSettingsProvider settings, int actorUserId)
{
    public void LoadInto(
        ObservableCollection<PosProductDesignItem> catalog,
        ObservableCollection<InventoryUnitRegistrationItem> registrations,
        ObservableCollection<string> categories)
    {
        var loadedProducts = new List<PosProductDesignItem>();
        var loadedUnits = new List<InventoryUnitRegistrationItem>();
        var loadedCategories = new List<string>();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT p.Id, p.Barcode, p.Sku, p.Name, c.Name, p.UnitName, p.CostPrice,
       p.RetailPrice, p.SpecialPrice, p.WholesalePrice, p.MinimumStockAlert,
       p.TrackSerialNumber, p.IsActiveForSale, p.Notes, p.RowVersion,
       CASE WHEN EXISTS (SELECT 1 FROM dbo.InventoryUnits x WHERE x.ProductId=p.Id)
            THEN (SELECT COUNT(*) FROM dbo.InventoryUnits x WHERE x.ProductId=p.Id AND x.UnitStatus=0)
            ELSE COALESCE((SELECT SUM(m.QuantityDelta) FROM dbo.StockMovements m WHERE m.ProductId=p.Id),0) END,
       (SELECT COUNT(*) FROM dbo.InventoryUnits x WHERE x.ProductId=p.Id AND x.UnitStatus=2)
FROM dbo.Products p JOIN dbo.Categories c ON c.Id=p.CategoryId
WHERE p.IsActiveForSale=1
ORDER BY p.Name;
SELECT iu.Id, iu.ProductId, p.Name, COALESCE(p.Barcode,N''), iu.PurchaseCost,
       COALESCE(s.Name,N'غير محدد'), iu.UnitStatus, iu.RegisteredAtUtc,
       COALESCE(iu.PurchaseInvoiceNumber,N''), COALESCE(iu.SaleInvoiceNumber,N''), COALESCE(iu.SerialNumber,N''),
       iu.DamageReason, iu.DamagedAtUtc, iu.DamageSettlementStatus, iu.RowVersion
FROM dbo.InventoryUnits iu
JOIN dbo.Products p ON p.Id=iu.ProductId
LEFT JOIN dbo.Suppliers s ON s.Id=iu.SupplierId
WHERE p.IsActiveForSale=1
ORDER BY iu.RegisteredAtUtc DESC, iu.Id DESC;
SELECT Name FROM dbo.Categories WHERE IsActive=1 ORDER BY Name;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            loadedProducts.Add(new PosProductDesignItem
            {
                Id = reader.GetInt32(0), Barcode = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Name = reader.GetString(3), Category = reader.GetString(4), Unit = reader.GetString(5),
                CostPrice = reader.GetDecimal(6), RetailPrice = reader.GetDecimal(7),
                SpecialPrice = reader.GetDecimal(8), WholesalePrice = reader.GetDecimal(9),
                MinStockAlert = reader.GetInt32(10), TrackSerialNumber = reader.GetBoolean(11),
                IsActiveForSale = reader.GetBoolean(12), Notes = reader.GetString(13),
                DatabaseRowVersion = (byte[])reader[14], AvailableStock = reader.GetInt32(15), DamagedCount = reader.GetInt32(16)
            });
        }
        reader.NextResult();
        while (reader.Read())
        {
            loadedUnits.Add(new InventoryUnitRegistrationItem
            {
                Id = reader.GetInt32(0), ProductId = reader.GetInt32(1), ProductName = reader.GetString(2),
                ProductBarcode = reader.GetString(3), PurchasePrice = reader.GetDecimal(4), SupplierName = reader.GetString(5),
                Status = (InventoryUnitStatus)reader.GetByte(6), RegisteredAt = AsLocal(reader.GetDateTime(7)),
                PurchaseInvoiceId = reader.GetString(8), SaleInvoiceNumber = reader.GetString(9), SerialNumber = reader.GetString(10),
                DamageReason = reader.GetString(11), DamagedAt = reader.IsDBNull(12) ? null : AsLocal(reader.GetDateTime(12)),
                DamageSettlementStatus = reader.GetString(13), DatabaseRowVersion = (byte[])reader[14]
            });
        }
        reader.NextResult();
        while (reader.Read()) loadedCategories.Add(reader.GetString(0));

        catalog.Clear(); registrations.Clear(); categories.Clear();
        foreach (var product in loadedProducts) catalog.Add(product);
        foreach (var unit in loadedUnits) registrations.Add(unit);
        foreach (var category in loadedCategories) categories.Add(category);
    }

    public void SaveNewProduct(PosProductDesignItem product, string supplierName, string invoiceNumber)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var categoryId = EnsureCategory(connection, transaction, product.Category);
            using (var command = CreateCommand(connection, transaction, @"
INSERT dbo.Products(CategoryId,Barcode,Sku,Name,UnitName,CostPrice,RetailPrice,SpecialPrice,WholesalePrice,
 MinimumStockAlert,TrackSerialNumber,IsActiveForSale,Notes)
OUTPUT inserted.Id, inserted.RowVersion
VALUES(@CategoryId,NULLIF(@Barcode,N''),NULL,@Name,@Unit,@Cost,@Retail,@Special,@Wholesale,@Min,@Track,@Active,@Notes);"))
            {
                AddProductParameters(command, product, categoryId);
                using var reader = command.ExecuteReader();
                if (!reader.Read()) throw new InventoryDataException("لم تُنشئ قاعدة البيانات الصنف الجديد.");
                product.Id = reader.GetInt32(0); product.DatabaseRowVersion = (byte[])reader[1];
            }
            if (product.AvailableStock > 0)
                SupplyCore(connection, transaction, product, product.AvailableStock, supplierName, product.CostPrice, invoiceNumber);
            Audit(connection, transaction, "CreateProduct", product.Id, $"إضافة صنف {product.Name}");
            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
    }

    public void UpdateProduct(PosProductDesignItem product)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var categoryId = EnsureCategory(connection, transaction, product.Category);
            using var command = CreateCommand(connection, transaction, @"
UPDATE dbo.Products SET CategoryId=@CategoryId,Barcode=NULLIF(@Barcode,N''),Name=@Name,UnitName=@Unit,
 CostPrice=@Cost,RetailPrice=@Retail,SpecialPrice=@Special,WholesalePrice=@Wholesale,
 MinimumStockAlert=@Min,TrackSerialNumber=@Track,IsActiveForSale=@Active,Notes=@Notes,UpdatedAtUtc=SYSUTCDATETIME()
OUTPUT inserted.RowVersion
WHERE Id=@Id AND (@RowVersion IS NULL OR RowVersion=@RowVersion);");
            AddProductParameters(command, product, categoryId);
            command.Parameters.AddWithValue("@Id", product.Id);
            command.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = (object?)product.DatabaseRowVersion ?? DBNull.Value;
            using var reader = command.ExecuteReader();
            if (!reader.Read()) throw Concurrency<Product>();
            product.DatabaseRowVersion = (byte[])reader[0];
            reader.Close();
            Audit(connection, transaction, "UpdateProduct", product.Id, $"تعديل صنف {product.Name}");
            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
    }

    public void Supply(PosProductDesignItem product, int quantity, string supplierName, decimal purchasePrice, string invoiceNumber)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            SupplyCore(connection, transaction, product, quantity, supplierName, purchasePrice, invoiceNumber);
            if (purchasePrice > 0)
            {
                using var command = CreateCommand(connection, transaction,
                    "UPDATE dbo.Products SET CostPrice=@Cost,UpdatedAtUtc=SYSUTCDATETIME() WHERE Id=@Id;");
                command.Parameters.AddWithValue("@Cost", purchasePrice); command.Parameters.AddWithValue("@Id", product.Id);
                command.ExecuteNonQuery();
            }
            Audit(connection, transaction, "SupplyStock", product.Id, $"توريد {quantity} من {product.Name} / {invoiceNumber}");
            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
    }

    private void SupplyCore(SqlConnection connection, SqlTransaction transaction, PosProductDesignItem product,
        int quantity, string supplierName, decimal purchasePrice, string invoiceNumber)
    {
        var supplierId = EnsureSupplier(connection, transaction, supplierName);
        for (var i = 0; i < quantity; i++)
        {
            int unitId;
            using (var command = CreateCommand(connection, transaction, @"
INSERT dbo.InventoryUnits(ProductId,SupplierId,PurchaseInvoiceNumber,PurchaseCost,UnitStatus)
OUTPUT inserted.Id VALUES(@ProductId,@SupplierId,@Invoice,@Cost,0);"))
            {
                command.Parameters.AddWithValue("@ProductId", product.Id); command.Parameters.AddWithValue("@SupplierId", supplierId);
                command.Parameters.AddWithValue("@Invoice", invoiceNumber); command.Parameters.AddWithValue("@Cost", purchasePrice);
                unitId = (int)command.ExecuteScalar();
            }
            StockMovement(connection, transaction, product.Id, unitId, "Supply", 1, purchasePrice, invoiceNumber);
        }
    }

    public void MarkDamaged(PosProductDesignItem product, int quantity, string? supplier, int? specificUnitId, string reason)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var ids = new List<int>();
            using (var command = CreateCommand(connection, transaction, @"
SELECT TOP (@Quantity) iu.Id FROM dbo.InventoryUnits iu WITH (UPDLOCK,HOLDLOCK)
LEFT JOIN dbo.Suppliers s ON s.Id=iu.SupplierId
WHERE iu.ProductId=@ProductId AND iu.UnitStatus=0
 AND (@SpecificId IS NULL OR iu.Id=@SpecificId)
 AND (@Supplier IS NULL OR s.Name=@Supplier)
ORDER BY iu.RegisteredAtUtc,iu.Id;"))
            {
                command.Parameters.AddWithValue("@Quantity", quantity); command.Parameters.AddWithValue("@ProductId", product.Id);
                command.Parameters.AddWithValue("@SpecificId", (object?)specificUnitId ?? DBNull.Value);
                command.Parameters.AddWithValue("@Supplier", string.IsNullOrWhiteSpace(supplier) ? DBNull.Value : supplier);
                using var reader = command.ExecuteReader(); while (reader.Read()) ids.Add(reader.GetInt32(0));
            }
            if (ids.Count != quantity) throw new InventoryDataException("الكمية المتاحة لهذا المورد أو التسجيل تغيرت. حدّث الشاشة وأعد المحاولة.");
            foreach (var id in ids) SetUnitDamaged(connection, transaction, id, reason);
            Audit(connection, transaction, "MarkDamaged", product.Id, $"هالك {quantity} من {product.Name}: {reason}");
            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
    }

    public void MarkSingleDamaged(InventoryUnitRegistrationItem unit, string reason)
    {
        using var connection = OpenConnection(); using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try { SetUnitDamaged(connection, transaction, unit.Id, reason); transaction.Commit(); }
        catch { transaction.Rollback(); throw; }
    }

    private void SetUnitDamaged(SqlConnection connection, SqlTransaction transaction, int unitId, string reason)
    {
        using (var command = CreateCommand(connection, transaction, @"
UPDATE dbo.InventoryUnits SET UnitStatus=2,DamageReason=@Reason,DamagedAtUtc=SYSUTCDATETIME(),DamageSettlementStatus=N'قيد المراجعة'
WHERE Id=@Id AND UnitStatus=0;"))
        {
            command.Parameters.AddWithValue("@Reason", reason); command.Parameters.AddWithValue("@Id", unitId);
            if (command.ExecuteNonQuery() != 1) throw Concurrency<InventoryUnitRegistrationItem>();
        }
        int productId; decimal cost;
        using (var command = CreateCommand(connection, transaction, "SELECT ProductId,PurchaseCost FROM dbo.InventoryUnits WHERE Id=@Id;"))
        { command.Parameters.AddWithValue("@Id", unitId); using var reader = command.ExecuteReader(); reader.Read(); productId = reader.GetInt32(0); cost = reader.GetDecimal(1); }
        StockMovement(connection, transaction, productId, unitId, "Damage", -1, cost, reason);
    }

    public void RegisterSerial(InventoryUnitRegistrationItem unit, string serial)
    {
        serial = serial.Trim();
        if (string.IsNullOrWhiteSpace(serial)) throw new InventoryDataException("أدخل رقم سيريال صحيحًا.");
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE dbo.InventoryUnits SET SerialNumber=@Serial
WHERE Id=@Id AND UnitStatus=0 AND (@RowVersion IS NULL OR RowVersion=@RowVersion);";
        command.Parameters.AddWithValue("@Serial", serial);
        command.Parameters.AddWithValue("@Id", unit.Id);
        command.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = (object?)unit.DatabaseRowVersion ?? DBNull.Value;
        if (command.ExecuteNonQuery() != 1) throw Concurrency<InventoryUnitRegistrationItem>();
        unit.SerialNumber = serial;
    }

    public void RestoreDamaged(InventoryUnitRegistrationItem unit)
    {
        using var connection = OpenConnection(); using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            int productId; decimal cost;
            using (var command = CreateCommand(connection, transaction, @"
UPDATE dbo.InventoryUnits SET UnitStatus=0,DamageReason=N'',DamagedAtUtc=NULL,DamageSettlementStatus=N''
OUTPUT inserted.ProductId,inserted.PurchaseCost WHERE Id=@Id AND UnitStatus=2;"))
            {
                command.Parameters.AddWithValue("@Id", unit.Id); using var reader = command.ExecuteReader();
                if (!reader.Read()) throw Concurrency<InventoryUnitRegistrationItem>(); productId = reader.GetInt32(0); cost = reader.GetDecimal(1);
            }
            StockMovement(connection, transaction, productId, unit.Id, "DamageRestore", 1, cost, "استرجاع هالك");
            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
    }

    public void MarkReturnedToSupplier(InventoryUnitRegistrationItem unit)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dbo.InventoryUnits SET DamageSettlementStatus=N'مرتجع للمورد' WHERE Id=@Id AND UnitStatus=2;";
        command.Parameters.AddWithValue("@Id", unit.Id);
        if (command.ExecuteNonQuery() != 1) throw Concurrency<InventoryUnitRegistrationItem>();
    }

    public void DeleteDamaged(InventoryUnitRegistrationItem unit)
    {
        using var connection = OpenConnection(); using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            using (var command = CreateCommand(connection, transaction, @"
IF EXISTS(SELECT 1 FROM dbo.SalesInvoiceLineUnits WHERE InventoryUnitId=@Id)
 OR EXISTS(SELECT 1 FROM dbo.PurchaseInvoiceLineUnits WHERE InventoryUnitId=@Id)
 OR EXISTS(SELECT 1 FROM dbo.ReturnExchangeUnits WHERE InventoryUnitId=@Id)
 THROW 50020,N'لا يمكن حذف وحدة مرتبطة بفاتورة أو مرتجع؛ احتفظ بسجلها للمراجعة.',1;
DELETE dbo.StockMovements WHERE InventoryUnitId=@Id;
DELETE dbo.InventoryUnits WHERE Id=@Id AND UnitStatus=2;
IF EXISTS(SELECT 1 FROM dbo.InventoryUnits WHERE Id=@Id)
 THROW 50021,N'تغيرت حالة الوحدة؛ حدّث الشاشة قبل حذفها.',1;"))
            { command.Parameters.AddWithValue("@Id", unit.Id); command.ExecuteNonQuery(); }
            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
    }

    /// <returns>True for a hard delete; false when history required archival.</returns>
    public bool DeleteOrArchiveProduct(PosProductDesignItem product)
    {
        using var connection = OpenConnection(); using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            bool hasHistory;
            using (var command = CreateCommand(connection, transaction, @"
SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.SalesInvoiceLines WHERE ProductId=@Id)
 OR EXISTS(SELECT 1 FROM dbo.PurchaseInvoiceLines WHERE ProductId=@Id)
 OR EXISTS(SELECT 1 FROM dbo.MaintenanceOrderParts WHERE ProductId=@Id)
 OR EXISTS(SELECT 1 FROM dbo.StockMovements WHERE ProductId=@Id) THEN 1 ELSE 0 END;"))
            { command.Parameters.AddWithValue("@Id", product.Id); hasHistory = (int)command.ExecuteScalar() == 1; }
            if (hasHistory)
            {
                using var command = CreateCommand(connection, transaction, "UPDATE dbo.Products SET IsActiveForSale=0,UpdatedAtUtc=SYSUTCDATETIME() WHERE Id=@Id;");
                command.Parameters.AddWithValue("@Id", product.Id); command.ExecuteNonQuery(); transaction.Commit(); return false;
            }
            using (var command = CreateCommand(connection, transaction,
                "DELETE dbo.StockMovements WHERE ProductId=@Id;DELETE dbo.InventoryUnits WHERE ProductId=@Id;DELETE dbo.Products WHERE Id=@Id;"))
            { command.Parameters.AddWithValue("@Id", product.Id); command.ExecuteNonQuery(); }
            transaction.Commit(); return true;
        }
        catch { transaction.Rollback(); throw; }
    }

    private int EnsureCategory(SqlConnection connection, SqlTransaction transaction, string name)
    {
        using (var command = CreateCommand(connection, transaction,
            "SELECT Id FROM dbo.Categories WITH (UPDLOCK,HOLDLOCK) WHERE Name=@Name;"))
        { command.Parameters.AddWithValue("@Name", name.Trim()); var value = command.ExecuteScalar(); if (value is not null) return (int)value; }
        using var insert = CreateCommand(connection, transaction, "INSERT dbo.Categories(Name) OUTPUT inserted.Id VALUES(@Name);");
        insert.Parameters.AddWithValue("@Name", name.Trim()); return (int)insert.ExecuteScalar();
    }

    private int EnsureSupplier(SqlConnection connection, SqlTransaction transaction, string name)
    {
        using (var command = CreateCommand(connection, transaction,
            "SELECT Id FROM dbo.Suppliers WITH (UPDLOCK,HOLDLOCK) WHERE Name=@Name;"))
        { command.Parameters.AddWithValue("@Name", name.Trim()); var value = command.ExecuteScalar(); if (value is not null) return (int)value; }
        using var insert = CreateCommand(connection, transaction, "INSERT dbo.Suppliers(Name) OUTPUT inserted.Id VALUES(@Name);");
        insert.Parameters.AddWithValue("@Name", name.Trim()); return (int)insert.ExecuteScalar();
    }

    private void StockMovement(SqlConnection connection, SqlTransaction transaction, int productId, int unitId,
        string type, int delta, decimal cost, string reason)
    {
        using var command = CreateCommand(connection, transaction, @"
INSERT dbo.StockMovements(ProductId,InventoryUnitId,MovementType,QuantityDelta,UnitCost,ActorUserId,SourceEntityType,SourceEntityId,Reason)
VALUES(@ProductId,@UnitId,@Type,@Delta,@Cost,@Actor,N'InventoryUnit',@UnitId,@Reason);");
        command.Parameters.AddWithValue("@ProductId", productId); command.Parameters.AddWithValue("@UnitId", unitId);
        command.Parameters.AddWithValue("@Type", type); command.Parameters.AddWithValue("@Delta", delta);
        command.Parameters.AddWithValue("@Cost", cost); command.Parameters.AddWithValue("@Actor", actorUserId);
        command.Parameters.AddWithValue("@Reason", reason); command.ExecuteNonQuery();
    }

    private void Audit(SqlConnection connection, SqlTransaction transaction, string action, int productId, string details)
    {
        using var command = CreateCommand(connection, transaction, @"
INSERT dbo.AuditLog(ActorUserId,Module,ActionName,EntityType,EntityId,NewValuesJson)
VALUES(@Actor,N'Inventory',@Action,N'Product',@EntityId,JSON_MODIFY(N'{}',N'$.details',@Details));");
        command.Parameters.AddWithValue("@Actor", actorUserId); command.Parameters.AddWithValue("@Action", action);
        command.Parameters.AddWithValue("@EntityId", productId.ToString()); command.Parameters.AddWithValue("@Details", details);
        command.ExecuteNonQuery();
    }

    private SqlConnection OpenConnection()
    {
        var connection = new SqlConnection(settings.GetConnectionString()); connection.Open(); return connection;
    }

    private static SqlCommand CreateCommand(SqlConnection connection, SqlTransaction transaction, string sql) =>
        new(sql, connection, transaction) { CommandTimeout = 30 };

    private static void AddProductParameters(SqlCommand command, PosProductDesignItem p, int categoryId)
    {
        command.Parameters.AddWithValue("@CategoryId", categoryId); command.Parameters.AddWithValue("@Barcode", p.Barcode.Trim());
        command.Parameters.AddWithValue("@Name", p.Name.Trim()); command.Parameters.AddWithValue("@Unit", p.Unit.Trim());
        command.Parameters.AddWithValue("@Cost", p.CostPrice); command.Parameters.AddWithValue("@Retail", p.RetailPrice);
        command.Parameters.AddWithValue("@Special", p.SpecialPrice); command.Parameters.AddWithValue("@Wholesale", p.WholesalePrice);
        command.Parameters.AddWithValue("@Min", p.MinStockAlert); command.Parameters.AddWithValue("@Track", p.TrackSerialNumber);
        command.Parameters.AddWithValue("@Active", p.IsActiveForSale); command.Parameters.AddWithValue("@Notes", p.Notes.Trim());
    }

    private static DateTime AsLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    private static InventoryDataException Concurrency<T>() =>
        new("تغير السجل بواسطة مستخدم آخر أو لم يعد بالحالة المطلوبة. حدّث شاشة المخزون ثم أعد المحاولة.");
}
