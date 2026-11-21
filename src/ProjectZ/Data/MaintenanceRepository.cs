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
    decimal SellPrice,
    string Barcode = "",
    string Unit = "قطعة",
    int? SupplierId = null,
    int ReorderQuantity = 0,
    string StorageLocation = "",
    string Notes = "");

/// <summary>A supplier row offered by the spare-parts screen (dbo.Suppliers).</summary>
public sealed record SparePartSupplierOption(int Id, string Name, string CompanyName, string Phone)
{
    public string DisplayName => string.IsNullOrWhiteSpace(CompanyName)
        ? Name
        : $"{Name} — {CompanyName}";

    public override string ToString() => DisplayName;
}

/// <summary>Result of creating a maintenance order.</summary>
public sealed record SavedMaintenanceOrder(
    int OrderId,
    string OrderNumber,
    decimal TotalDepositPaid,
    byte[]? RowVersion = null);

/// <summary>The exact ticket/version that was shown, not whatever is selected after a refresh.</summary>
public sealed record MaintenanceTicketKey(int Id, byte[]? Version)
{
    public static MaintenanceTicketKey Open(MaintenanceOrderDesignItem order) => new(order.Id, order.DatabaseRowVersion?.ToArray());
}

public sealed record MaintenanceDelivery(MaintenanceTicketKey Ticket, decimal Labor, decimal Discount, decimal ExpectedTotal,
    decimal Cash, decimal Vodafone, decimal InstaPay, bool RefundExcess, int WarrantyDays, string Notes);

/// <summary>Only a committed SQL snapshot can be used to update an open invoice or print its warranty.</summary>
public interface IMaintenanceSettlement
{
    byte[] Deliver(MaintenanceDelivery request);
    byte[] CollectDeposit(MaintenanceTicketKey key, FinancialAccountKind account, decimal amount);
    MaintenanceOrderDesignItem LoadTicket(int id);
    DateTime AppendMaintenanceActivity(MaintenanceTicketKey key, byte mode);
}

/// <summary>SQL Server persistence for maintenance orders, spare parts, the movement ledger and order-part lines.</summary>
public sealed class MaintenanceRepository(ConnectionSettingsProvider settings, int actorUserId) : IMaintenanceSettlement
{
    private readonly PasscodeProtector passcodeProtector = new(settings.GetPasscodeKey());

    /// <summary>True when an AES key is configured, so device passcodes are stored encrypted.</summary>
    public bool IsPasscodeProtectionEnabled => passcodeProtector.IsEnabled;

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
SELECT sp.Id, sp.Name, ISNULL(sp.Category,N''), sp.Quantity, sp.MinimumStockAlert, sp.CostPrice, sp.SellPrice,
       ISNULL(sp.Barcode,N''), ISNULL(sp.Unit,N'قطعة'), sp.SupplierId,
       ISNULL(NULLIF(LTRIM(RTRIM(s.Name)),N''), N'') AS SupplierName,
       sp.ReorderQuantity, ISNULL(sp.StorageLocation,N''), ISNULL(sp.Notes,N''),
       (SELECT MAX(m.OccurredAtUtc) FROM dbo.MaintenanceSparePartMovements AS m WHERE m.SparePartId = sp.Id) AS LastMovementAtUtc
FROM dbo.MaintenanceSpareParts AS sp
LEFT JOIN dbo.Suppliers AS s ON s.Id = sp.SupplierId
WHERE sp.IsActive = 1
ORDER BY sp.Id DESC;";
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
                    SellPrice = reader.GetDecimal(6),
                    Barcode = reader.GetString(7),
                    Unit = reader.GetString(8),
                    SupplierId = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                    SupplierName = reader.GetString(10),
                    ReorderQuantity = reader.GetInt32(11),
                    StorageLocation = reader.GetString(12),
                    Notes = reader.GetString(13),
                    LastMovementAt = reader.IsDBNull(14) ? null : reader.GetDateTime(14).ToLocalTime()
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

    /// <summary>Loads the active suppliers for the preferred-supplier and restock pickers.</summary>
    public List<SparePartSupplierOption> LoadSparePartSuppliers()
    {
        var suppliers = new List<SparePartSupplierOption>();
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT s.Id, s.Name, ISNULL(s.CompanyName,N''), ISNULL(s.Phone,N'')
FROM dbo.Suppliers AS s
WHERE s.IsActive = 1
   OR EXISTS (SELECT 1 FROM dbo.MaintenanceSpareParts AS sp WHERE sp.SupplierId = s.Id)
ORDER BY s.Name;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                suppliers.Add(new SparePartSupplierOption(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3)));
            }
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            throw Describe(ex, "تعذّر تحميل قائمة الموردين من قاعدة البيانات.");
        }

        return suppliers;
    }

    /// <summary>
    /// Adds a new spare part or updates an existing one; returns the persisted row. Every quantity
    /// difference is written to dbo.MaintenanceSparePartMovements in the same transaction, so a
    /// manual correction can never change the balance without an audit row.
    /// </summary>
    public SavedSparePart SaveSparePart(
        int? existingPartId,
        string name,
        string category,
        int quantity,
        int minimumStockAlert,
        decimal costPrice,
        decimal sellPrice,
        string barcode = "",
        string unit = "قطعة",
        int? supplierId = null,
        int reorderQuantity = 0,
        string storageLocation = "",
        string notes = "")
    {
        PermissionContext.Demand(existingPartId is null ? "MAINTENANCE_PARTS.CREATE" : "MAINTENANCE_PARTS.EDIT");
        name = Trim(name, 250, "");
        category = Trim(category, 100, "شاشات");
        quantity = Math.Max(0, quantity);
        minimumStockAlert = Math.Max(0, minimumStockAlert);
        costPrice = Math.Max(0m, costPrice);
        sellPrice = Math.Max(0m, sellPrice);
        barcode = Trim(barcode, 100, "");
        unit = Trim(unit, 30, "قطعة");
        reorderQuantity = Math.Max(0, reorderQuantity);
        storageLocation = Trim(storageLocation, 120, "");
        notes = Trim(notes, 1000, "");
        if (supplierId is <= 0) supplierId = null;

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

            if (existingPartId is null)
            {
                int partId;
                using (var insert = CreateCommand(connection, transaction, @"
INSERT dbo.MaintenanceSpareParts(Name, Category, Barcode, Unit, SupplierId, Quantity, MinimumStockAlert,
                                 ReorderQuantity, StorageLocation, Notes, CostPrice, SellPrice, IsActive)
OUTPUT inserted.Id
VALUES(@Name,@Category,@Barcode,@Unit,@Supplier,@Qty,@Min,@Reorder,@Location,@Notes,@Cost,@Sell,1);"))
                {
                    insert.Parameters.Add("@Name", SqlDbType.NVarChar, 250).Value = name;
                    insert.Parameters.Add("@Category", SqlDbType.NVarChar, 100).Value = category;
                    insert.Parameters.Add("@Barcode", SqlDbType.NVarChar, 100).Value = barcode.Length == 0 ? DBNull.Value : barcode;
                    insert.Parameters.Add("@Unit", SqlDbType.NVarChar, 30).Value = unit;
                    insert.Parameters.Add("@Supplier", SqlDbType.Int).Value = supplierId is null ? DBNull.Value : supplierId.Value;
                    insert.Parameters.Add("@Qty", SqlDbType.Int).Value = quantity;
                    insert.Parameters.Add("@Min", SqlDbType.Int).Value = minimumStockAlert;
                    insert.Parameters.Add("@Reorder", SqlDbType.Int).Value = reorderQuantity;
                    insert.Parameters.Add("@Location", SqlDbType.NVarChar, 120).Value = storageLocation;
                    insert.Parameters.Add("@Notes", SqlDbType.NVarChar, 1000).Value = notes;
                    AddMoney(insert, "@Cost", costPrice);
                    AddMoney(insert, "@Sell", sellPrice);
                    partId = (int)insert.ExecuteScalar()!;
                }

                if (quantity > 0)
                {
                    InsertSparePartMovement(
                        connection, transaction, partId, MaintenanceSparePartMovementKind.OpeningBalance,
                        quantity, 0, quantity, costPrice, supplierId, 0, barcode,
                        "الرصيد الافتتاحي عند تسجيل قطعة الغيار.");
                }

                transaction.Commit();
                return new SavedSparePart(partId, name, category, quantity, minimumStockAlert, costPrice, sellPrice,
                    barcode, unit, supplierId, reorderQuantity, storageLocation, notes);
            }

            int beforeQuantity;
            using (var lockRow = CreateCommand(connection, transaction, @"
SELECT Quantity FROM dbo.MaintenanceSpareParts WITH (UPDLOCK, HOLDLOCK)
WHERE Id = @Id AND IsActive = 1;"))
            {
                lockRow.Parameters.Add("@Id", SqlDbType.Int).Value = existingPartId.Value;
                var value = lockRow.ExecuteScalar();
                if (value is null)
                    throw new MaintenanceDataException("تعذّر تعديل قطعة الغيار: غير موجودة أو متوقفة.");
                beforeQuantity = Convert.ToInt32(value);
            }

            using (var update = CreateCommand(connection, transaction, @"
UPDATE dbo.MaintenanceSpareParts
SET Name = @Name, Category = @Category, Barcode = @Barcode, Unit = @Unit, SupplierId = @Supplier,
    Quantity = @Qty, MinimumStockAlert = @Min, ReorderQuantity = @Reorder, StorageLocation = @Location,
    Notes = @Notes, CostPrice = @Cost, SellPrice = @Sell, UpdatedAtUtc = SYSUTCDATETIME()
WHERE Id = @Id AND IsActive = 1;"))
            {
                update.Parameters.Add("@Name", SqlDbType.NVarChar, 250).Value = name;
                update.Parameters.Add("@Category", SqlDbType.NVarChar, 100).Value = category;
                update.Parameters.Add("@Barcode", SqlDbType.NVarChar, 100).Value = barcode.Length == 0 ? DBNull.Value : barcode;
                update.Parameters.Add("@Unit", SqlDbType.NVarChar, 30).Value = unit;
                update.Parameters.Add("@Supplier", SqlDbType.Int).Value = supplierId is null ? DBNull.Value : supplierId.Value;
                update.Parameters.Add("@Qty", SqlDbType.Int).Value = quantity;
                update.Parameters.Add("@Min", SqlDbType.Int).Value = minimumStockAlert;
                update.Parameters.Add("@Reorder", SqlDbType.Int).Value = reorderQuantity;
                update.Parameters.Add("@Location", SqlDbType.NVarChar, 120).Value = storageLocation;
                update.Parameters.Add("@Notes", SqlDbType.NVarChar, 1000).Value = notes;
                AddMoney(update, "@Cost", costPrice);
                AddMoney(update, "@Sell", sellPrice);
                update.Parameters.Add("@Id", SqlDbType.Int).Value = existingPartId.Value;
                if (update.ExecuteNonQuery() == 0)
                    throw new MaintenanceDataException("تعذّر تعديل قطعة الغيار: غير موجودة أو متوقفة.");
            }

            if (beforeQuantity != quantity)
            {
                var kind = quantity > beforeQuantity
                    ? MaintenanceSparePartMovementKind.AdjustmentIncrease
                    : MaintenanceSparePartMovementKind.AdjustmentDecrease;
                InsertSparePartMovement(
                    connection, transaction, existingPartId.Value, kind,
                    Math.Abs(quantity - beforeQuantity), beforeQuantity, quantity, costPrice, supplierId, 0, barcode,
                    "تعديل يدوي للرصيد من شاشة قطع الغيار.");
            }

            transaction.Commit();
            return new SavedSparePart(existingPartId.Value, name, category, quantity, minimumStockAlert, costPrice, sellPrice,
                barcode, unit, supplierId, reorderQuantity, storageLocation, notes);
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر حفظ قطعة الغيار.");
        }
    }

    /// <summary>
    /// Deletes a spare part. A part that has been used in an order or has stock movements is
    /// deactivated (IsActive = 0) instead of being removed, to preserve the audit trail; an
    /// untouched part is deleted permanently.
    /// </summary>
    public (bool Deleted, string Message) DeleteSparePart(int partId, string name)
    {
        PermissionContext.Demand("MAINTENANCE_PARTS.DELETE");
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

            int historyCount;
            int quantity;
            using (var count = CreateCommand(connection, transaction, @"
SELECT sp.Quantity,
       (SELECT COUNT(1) FROM dbo.MaintenanceOrderParts WHERE SparePartId = @Id)
     + (SELECT COUNT(1) FROM dbo.MaintenanceSparePartMovements WHERE SparePartId = @Id)
FROM dbo.MaintenanceSpareParts AS sp
WHERE sp.Id = @Id AND sp.IsActive = 1;"))
            {
                count.Parameters.Add("@Id", SqlDbType.Int).Value = partId;
                using var reader = count.ExecuteReader();
                if (!reader.Read())
                    throw new MaintenanceDataException("تعذّر حذف قطعة الغيار: غير موجودة أو متوقفة.");
                quantity = reader.GetInt32(0);
                historyCount = reader.GetInt32(1);
            }

            if (historyCount == 0 && quantity == 0)
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
            return (false, $"يوجد سجل صرف أو حركات مخزون أو رصيد متبقٍ لـ «{name}»، تم إيقافه بدل الحذف النهائي للحفاظ على السجل.");
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر حذف قطعة الغيار.");
        }
    }

    /// <summary>
    /// Adds stock to a spare part (a purchase from a supplier). In one transaction it raises the
    /// quantity, keeps the moving-average cost, links the preferred supplier when supplied and
    /// appends a Purchase movement with the supplier, reference and note.
    /// </summary>
    public SavedSparePart RestockSparePart(
        int partId,
        int quantity,
        decimal unitCost,
        int? supplierId,
        string reference,
        string note)
    {
        PermissionContext.Demand("MAINTENANCE_PARTS.RESTOCK");
        quantity = Math.Max(1, quantity);
        unitCost = Math.Max(0m, unitCost);
        reference = Trim(reference, 100, "");
        note = Trim(note, 1000, "");
        if (supplierId is <= 0) supplierId = null;

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);

            string name, category, barcode, unit, storageLocation, notes;
            int beforeQuantity, minimumStockAlert, reorderQuantity;
            decimal currentCost, sellPrice;
            int? currentSupplierId;
            using (var lockRow = CreateCommand(connection, transaction, @"
SELECT Name, ISNULL(Category,N''), ISNULL(Barcode,N''), ISNULL(Unit,N'قطعة'), Quantity, MinimumStockAlert,
       ReorderQuantity, CostPrice, SellPrice, SupplierId, ISNULL(StorageLocation,N''), ISNULL(Notes,N'')
FROM dbo.MaintenanceSpareParts WITH (UPDLOCK, HOLDLOCK)
WHERE Id = @Id AND IsActive = 1;"))
            {
                lockRow.Parameters.Add("@Id", SqlDbType.Int).Value = partId;
                using var reader = lockRow.ExecuteReader();
                if (!reader.Read())
                    throw new MaintenanceDataException("تعذّر توريد القطعة: قطعة الغيار غير موجودة أو متوقفة.");
                name = reader.GetString(0);
                category = reader.GetString(1);
                barcode = reader.GetString(2);
                unit = reader.GetString(3);
                beforeQuantity = reader.GetInt32(4);
                minimumStockAlert = reader.GetInt32(5);
                reorderQuantity = reader.GetInt32(6);
                currentCost = reader.GetDecimal(7);
                sellPrice = reader.GetDecimal(8);
                currentSupplierId = reader.IsDBNull(9) ? null : reader.GetInt32(9);
                storageLocation = reader.GetString(10);
                notes = reader.GetString(11);
            }

            var effectiveSupplierId = supplierId ?? currentSupplierId;
            var newCost = currentCost;
            var totalUnits = beforeQuantity + quantity;
            if (unitCost > 0m && totalUnits > 0)
                newCost = Math.Round(((beforeQuantity * currentCost) + (quantity * unitCost)) / totalUnits, 2, MidpointRounding.AwayFromZero);
            else if (unitCost > 0m && beforeQuantity == 0)
                newCost = unitCost;

            var newQuantity = beforeQuantity + quantity;
            using (var update = CreateCommand(connection, transaction, @"
UPDATE dbo.MaintenanceSpareParts
SET Quantity = @Qty, CostPrice = @Cost, SupplierId = @Supplier,
    LastPurchaseAtUtc = SYSUTCDATETIME(), UpdatedAtUtc = SYSUTCDATETIME()
WHERE Id = @Id AND IsActive = 1;"))
            {
                update.Parameters.Add("@Qty", SqlDbType.Int).Value = newQuantity;
                AddMoney(update, "@Cost", newCost);
                update.Parameters.Add("@Supplier", SqlDbType.Int).Value = effectiveSupplierId is null ? DBNull.Value : effectiveSupplierId.Value;
                update.Parameters.Add("@Id", SqlDbType.Int).Value = partId;
                update.ExecuteNonQuery();
            }

            InsertSparePartMovement(
                connection, transaction, partId, MaintenanceSparePartMovementKind.Purchase,
                quantity, beforeQuantity, newQuantity, unitCost, effectiveSupplierId, 0, reference,
                string.IsNullOrWhiteSpace(note) ? "توريد قطعة غيار من المورد." : note);

            transaction.Commit();
            return new SavedSparePart(partId, name, category, newQuantity, minimumStockAlert, newCost, sellPrice,
                barcode, unit, effectiveSupplierId, reorderQuantity, storageLocation, notes);
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر توريد قطعة الغيار.");
        }
    }

    /// <summary>Loads the movement audit trail (newest first), optionally for one spare part.</summary>
    public void LoadSparePartMovementsInto(
        ObservableCollection<MaintenanceSparePartMovementItem> target,
        int? sparePartId = null,
        int limit = 500)
    {
        var loaded = new List<MaintenanceSparePartMovementItem>();
        limit = Math.Clamp(limit, 1, 2000);
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT TOP (@Limit)
       m.Id, m.SparePartId, sp.Name, m.MovementKind, m.Quantity, m.QuantityBefore, m.QuantityAfter, m.UnitCost,
       m.SupplierId, ISNULL(NULLIF(LTRIM(RTRIM(s.Name)),N''), N'') AS SupplierName,
       m.MaintenanceOrderId, ISNULL(mo.OrderNumber,N'') AS OrderNumber,
       m.CustomerId, ISNULL(NULLIF(LTRIM(RTRIM(c.Name)),N''), N'') AS CustomerName,
       ISNULL(m.Reference,N''), ISNULL(m.Note,N''),
       ISNULL(NULLIF(LTRIM(RTRIM(u.DisplayName)),N''), N'') AS ActorName, m.OccurredAtUtc
FROM dbo.MaintenanceSparePartMovements AS m
INNER JOIN dbo.MaintenanceSpareParts AS sp ON sp.Id = m.SparePartId
LEFT JOIN dbo.Suppliers AS s ON s.Id = m.SupplierId
LEFT JOIN dbo.MaintenanceOrders AS mo ON mo.Id = m.MaintenanceOrderId
LEFT JOIN dbo.Customers AS c ON c.Id = m.CustomerId
LEFT JOIN dbo.Users AS u ON u.Id = m.ActorUserId
WHERE (@PartId IS NULL OR m.SparePartId = @PartId)
ORDER BY m.OccurredAtUtc DESC, m.Id DESC;";
            command.Parameters.Add("@Limit", SqlDbType.Int).Value = limit;
            command.Parameters.Add("@PartId", SqlDbType.Int).Value = sparePartId is > 0 ? sparePartId.Value : DBNull.Value;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var partId = reader.GetInt32(1);
                loaded.Add(new MaintenanceSparePartMovementItem
                {
                    Id = reader.GetInt32(0),
                    SparePartId = partId,
                    SparePartName = reader.GetString(2),
                    SparePartCode = $"SPR-{partId:000}",
                    Kind = (MaintenanceSparePartMovementKind)reader.GetByte(3),
                    Quantity = reader.GetInt32(4),
                    QuantityBefore = reader.GetInt32(5),
                    QuantityAfter = reader.GetInt32(6),
                    UnitCost = reader.GetDecimal(7),
                    SupplierId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    SupplierName = reader.GetString(9),
                    MaintenanceOrderId = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    OrderNumber = reader.GetString(11),
                    CustomerId = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                    CustomerName = reader.GetString(13),
                    Reference = reader.GetString(14),
                    Note = reader.GetString(15),
                    ActorName = reader.GetString(16),
                    OccurredAt = reader.GetDateTime(17).ToLocalTime()
                });
            }
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            throw Describe(ex, "تعذّر تحميل سجل حركات قطع الغيار من قاعدة البيانات.");
        }

        target.Clear();
        foreach (var item in loaded) target.Add(item);
    }

    /// <summary>Appends one audit row to dbo.MaintenanceSparePartMovements inside the caller's transaction.</summary>
    private void InsertSparePartMovement(
        SqlConnection connection,
        SqlTransaction transaction,
        int sparePartId,
        MaintenanceSparePartMovementKind kind,
        int quantity,
        int quantityBefore,
        int quantityAfter,
        decimal unitCost,
        int? supplierId,
        int orderId,
        string reference,
        string note)
    {
        using var command = CreateCommand(connection, transaction, @"
INSERT dbo.MaintenanceSparePartMovements(SparePartId, MovementKind, Quantity, QuantityBefore, QuantityAfter,
    UnitCost, SupplierId, MaintenanceOrderId, CustomerId, Reference, Note, ActorUserId, OccurredAtUtc)
SELECT @Part, @Kind, @Qty, @Before, @After, @Cost, COALESCE(@Supplier, sp.SupplierId),
       CASE WHEN @Order > 0 THEN @Order END,
       CASE WHEN @Order > 0 THEN mo.CustomerId END, @Ref, @Note, @Actor, SYSUTCDATETIME()
FROM dbo.MaintenanceSpareParts AS sp
LEFT JOIN dbo.MaintenanceOrders AS mo ON mo.Id = @Order
WHERE sp.Id = @Part;");
        command.Parameters.Add("@Part", SqlDbType.Int).Value = sparePartId;
        command.Parameters.Add("@Kind", SqlDbType.TinyInt).Value = (byte)kind;
        command.Parameters.Add("@Qty", SqlDbType.Int).Value = quantity;
        command.Parameters.Add("@Before", SqlDbType.Int).Value = quantityBefore;
        command.Parameters.Add("@After", SqlDbType.Int).Value = quantityAfter;
        AddMoney(command, "@Cost", unitCost);
        command.Parameters.Add("@Supplier", SqlDbType.Int).Value = supplierId is null ? DBNull.Value : supplierId.Value;
        command.Parameters.Add("@Order", SqlDbType.Int).Value = Math.Max(0, orderId);
        command.Parameters.Add("@Ref", SqlDbType.NVarChar, 100).Value = reference;
        command.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value = note;
        command.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
        command.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------- maintenance orders

    /// <summary>
    /// Creates a maintenance order (intake) with its activity log and deposit payments in one
    /// transaction. The order number is taken from DocumentSequences; the deposit payments are
    /// written to MaintenancePayments against the cash / Vodafone Cash / InstaPay accounts.
    /// </summary>
    public SavedMaintenanceOrder CreateMaintenanceOrder(MaintenanceOrderDesignItem order)
    {
        PermissionContext.Demand("MAINTENANCE.CREATE");
        try
        {
            using var connection = OpenConnection();
            using var insert = new SqlCommand("dbo.OpenMaintenanceTicket", connection) { CommandType = CommandType.StoredProcedure };
            insert.Parameters.Add("@CustomerId", SqlDbType.Int).Value = order.IsWalkInCustomer ? DBNull.Value : (object)order.CustomerId;
            insert.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value = order.CustomerName;
            insert.Parameters.Add("@Phone", SqlDbType.NVarChar, 30).Value = order.CustomerPhone;
            insert.Parameters.Add("@Phone2", SqlDbType.NVarChar, 30).Value = order.CustomerSecondaryPhone;
            insert.Parameters.Add("@Address", SqlDbType.NVarChar, 500).Value = order.CustomerAddress;
            insert.Parameters.Add("@WalkIn", SqlDbType.Bit).Value = order.IsWalkInCustomer;
            insert.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
            insert.Parameters.Add("@Priority", SqlDbType.TinyInt).Value = (byte)order.Priority;
            insert.Parameters.Add("@Tech", SqlDbType.NVarChar, 150).Value = order.AssignedTechnician;
            insert.Parameters.Add("@Expected", SqlDbType.DateTime2).Value = (object?)order.ExpectedDeliveryAt?.ToUniversalTime() ?? DBNull.Value;
            insert.Parameters.Add("@ExpectedLabel", SqlDbType.NVarChar, 200).Value = order.ExpectedDeliveryLabel;
            insert.Parameters.Add("@DeviceType", SqlDbType.NVarChar, 100).Value = order.DeviceType;
            insert.Parameters.Add("@Brand", SqlDbType.NVarChar, 100).Value = order.DeviceBrand;
            insert.Parameters.Add("@Model", SqlDbType.NVarChar, 150).Value = order.DeviceModel;
            insert.Parameters.Add("@Color", SqlDbType.NVarChar, 80).Value = order.DeviceColor;
            insert.Parameters.Add("@Serial", SqlDbType.NVarChar, 150).Value = order.SerialOrImei;
            insert.Parameters.Add("@PasscodeType", SqlDbType.TinyInt).Value = (byte)order.PasscodeType;
            insert.Parameters.Add("@Passcode", SqlDbType.NVarChar, 1000).Value = passcodeProtector.Protect(order.PasscodeValue);
            insert.Parameters.Add("@Fault", SqlDbType.NVarChar, 2000).Value = order.ReportedFault;
            insert.Parameters.Add("@Physical", SqlDbType.NVarChar, 1500).Value = order.PhysicalCondition;
            insert.Parameters.Add("@Accessories", SqlDbType.NVarChar, 1000).Value = order.AccessoriesReceived;
            insert.Parameters.Add("@Intake", SqlDbType.NVarChar, 1500).Value = order.IntakeNotes;
            insert.Parameters.Add("@EstimatePending", SqlDbType.Bit).Value = order.IsEstimatePendingInspection;
            AddMoney(insert, "@EstimatedCost", order.EstimatedCost);
            AddTenders(insert, order.DepositCashPaid, order.DepositVodafonePaid, order.DepositInstaPayPaid);
            using var reader = insert.ExecuteReader();
            if (!reader.Read()) throw new MaintenanceDataException("لم يصل إيصال الاستلام. تحقّق من السجل قبل إعادة المحاولة.");
            order.ReceivedAt = reader.GetDateTime(2).ToLocalTime();
            order.DatabaseRowVersion = (byte[])reader[3];
            return new SavedMaintenanceOrder(reader.GetInt32(0), reader.GetString(1), order.TotalDepositPaid, order.DatabaseRowVersion);
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر تسجيل أمر الصيانة.");
        }
    }

    /// <summary>
    /// Reads the next order number from dbo.DocumentSequences without consuming it, so the intake
    /// screen can show the real MNT-yyyy-NNNN badge instead of guessing from the loaded rows.
    /// </summary>
    public string PeekNextOrderNumber()
    {
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT YEAR(SYSDATETIME()), ISNULL((SELECT LastValue FROM dbo.DocumentSequences
               WHERE DocumentType = @Type AND SequenceYear = YEAR(SYSDATETIME())), 0) + 1;";
            command.Parameters.Add("@Type", SqlDbType.NVarChar, 30).Value = "MaintenanceOrder";
            using var reader = command.ExecuteReader();
            reader.Read();
            return $"MNT-{reader.GetInt32(0):0000}-{reader.GetInt64(1):0000}";
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر قراءة رقم أمر الصيانة القادم من قاعدة البيانات.");
        }
    }

    /// <summary>
    /// Detects another still-open order for the same serial/IMEI so the same device is not received
    /// twice into the workshop by mistake. Delivered (5) and cancelled (6) orders are ignored.
    /// </summary>
    public bool HasOpenOrderWithSerial(string serialOrImei)
    {
        var serial = serialOrImei.Trim();
        if (serial.Length == 0) return false;

        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM dbo.MaintenanceOrders
    WHERE SerialOrImei = @Serial AND OrderStatus NOT IN (5, 6)) THEN 1 ELSE 0 END;";
            command.Parameters.Add("@Serial", SqlDbType.NVarChar, 150).Value = serial;
            return Convert.ToInt32(command.ExecuteScalar() ?? 0) == 1;
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر التحقق من الرقم التسلسلي في قاعدة البيانات.");
        }
    }

    /// <summary>
    /// Inserts a single activity row (a print preview, for example) without touching the order
    /// columns and without re-syncing its part lines, or changing its version.
    /// </summary>
    public DateTime AppendMaintenanceActivity(MaintenanceTicketKey key, byte mode)
    {
        PermissionContext.Demand("MAINTENANCE.PRINT");
        return PostScalar<DateTime>("dbo.AppendMaintenancePrint", key,
            cmd => cmd.Parameters.Add("@Mode", SqlDbType.TinyInt).Value = mode).ToLocalTime();
    }

    public byte[] SaveWorkshop(MaintenanceOrderDesignItem order, MaintenanceOrderStatus status, MaintenancePriority priority,
        string technician, DateTime? expected, string expectedLabel, string diagnosis, decimal labor, decimal discount)
    {
        PermissionContext.Demand("MAINTENANCE.WORKSHOP");
        return Post("dbo.SaveMaintenanceWorkshop", MaintenanceTicketKey.Open(order), cmd =>
        {
            cmd.Parameters.Add("@Status", SqlDbType.TinyInt).Value = (byte)status;
            cmd.Parameters.Add("@Priority", SqlDbType.TinyInt).Value = (byte)priority;
            cmd.Parameters.Add("@Tech", SqlDbType.NVarChar, 150).Value = technician;
            cmd.Parameters.Add("@Expected", SqlDbType.DateTime2).Value = (object?)expected?.ToUniversalTime() ?? DBNull.Value;
            cmd.Parameters.Add("@ExpectedLabel", SqlDbType.NVarChar, 200).Value = expectedLabel;
            cmd.Parameters.Add("@Diagnosis", SqlDbType.NVarChar, 2000).Value = diagnosis;
            AddMoney(cmd, "@Labor", labor); AddMoney(cmd, "@Discount", discount);
        });
    }

    public byte[] IssuePart(MaintenanceTicketKey key, int spare, int qty, decimal labor, decimal discount)
    {
        PermissionContext.Demand("MAINTENANCE.PARTS_ISSUE");
        return ChangePart(key, 1, labor, discount, cmd =>
        {
            cmd.Parameters.Add("@Spare", SqlDbType.Int).Value = spare;
            cmd.Parameters.Add("@Qty", SqlDbType.Int).Value = qty;
        });
    }

    public byte[] AddManualPart(MaintenanceTicketKey key, string name, int qty, decimal cost, decimal price, decimal labor, decimal discount)
    {
        PermissionContext.Demand("MAINTENANCE.PARTS_ISSUE");
        return ChangePart(key, 2, labor, discount, cmd =>
        {
            cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 250).Value = name;
            cmd.Parameters.Add("@Qty", SqlDbType.Int).Value = qty;
            AddMoney(cmd, "@Cost", cost); AddMoney(cmd, "@Price", price);
        });
    }

    public byte[] RemovePart(MaintenanceTicketKey key, int lineId, decimal labor, decimal discount)
    {
        PermissionContext.Demand("MAINTENANCE.PARTS_ISSUE");
        return ChangePart(key, 3, labor, discount, cmd => cmd.Parameters.Add("@Line", SqlDbType.Int).Value = lineId);
    }

    private byte[] ChangePart(MaintenanceTicketKey key, byte action, decimal labor, decimal discount, Action<SqlCommand> bind) =>
        Post("dbo.ChangeMaintenancePart", key, cmd =>
        {
            cmd.Parameters.Add("@Action", SqlDbType.TinyInt).Value = action;
            AddMoney(cmd, "@Labor", labor); AddMoney(cmd, "@Discount", discount); bind(cmd);
        });

    public byte[] CollectDeposit(MaintenanceTicketKey key, FinancialAccountKind account, decimal amount)
    {
        PermissionContext.Demand("MAINTENANCE.COLLECT");
        return Post("dbo.SettleMaintenanceTicket", key, cmd =>
        {
            cmd.Parameters.Add("@Action", SqlDbType.TinyInt).Value = (byte)1;
            AddTenders(cmd, account == FinancialAccountKind.Cash ? amount : 0,
                account == FinancialAccountKind.VodafoneCash ? amount : 0,
                account == FinancialAccountKind.InstaPay ? amount : 0);
        });
    }

    public byte[] Deliver(MaintenanceDelivery request)
    {
        PermissionContext.Demand("MAINTENANCE.DELIVER");
        return Post("dbo.SettleMaintenanceTicket", request.Ticket, cmd =>
        {
            cmd.Parameters.Add("@Action", SqlDbType.TinyInt).Value = (byte)2;
            AddTenders(cmd, request.Cash, request.Vodafone, request.InstaPay);
            AddMoney(cmd, "@Labor", request.Labor); AddMoney(cmd, "@Discount", request.Discount);
            AddMoney(cmd, "@ExpectedTotal", request.ExpectedTotal);
            cmd.Parameters.Add("@RefundExcess", SqlDbType.Bit).Value = request.RefundExcess;
            cmd.Parameters.Add("@Warranty", SqlDbType.Int).Value = request.WarrantyDays;
            cmd.Parameters.Add("@Notes", SqlDbType.NVarChar, 1500).Value = request.Notes;
        });
    }

    public byte[] Cancel(MaintenanceTicketKey key, decimal refund, bool returnParts, string reason)
    {
        PermissionContext.Demand("MAINTENANCE.CANCEL");
        return Post("dbo.SettleMaintenanceTicket", key, cmd =>
        {
            cmd.Parameters.Add("@Action", SqlDbType.TinyInt).Value = (byte)3;
            AddMoney(cmd, "@CancelRefund", refund);
            cmd.Parameters.Add("@ReturnParts", SqlDbType.Bit).Value = returnParts;
            cmd.Parameters.Add("@Notes", SqlDbType.NVarChar, 1500).Value = reason;
        });
    }

    public byte[] Reopen(MaintenanceTicketKey key, string reason)
    {
        PermissionContext.Demand("MAINTENANCE.REOPEN");
        return Post("dbo.ReopenMaintenanceTicket", key,
            cmd => cmd.Parameters.Add("@Reason", SqlDbType.NVarChar, 1500).Value = reason);
    }

    private byte[] Post(string procedure, MaintenanceTicketKey key, Action<SqlCommand> bind) => PostScalar<byte[]>(procedure, key, bind);

    private T PostScalar<T>(string procedure, MaintenanceTicketKey key, Action<SqlCommand> bind)
    {
        if (key.Version is not { Length: 8 }) throw new MaintenanceDataException("نسخة الأمر غير متاحة. حدّث القائمة وافتح الأمر من جديد.");
        try
        {
            using var connection = OpenConnection();
            using var cmd = new SqlCommand(procedure, connection) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = key.Id;
            cmd.Parameters.Add("@Version", SqlDbType.VarBinary, 8).Value = key.Version;
            cmd.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
            bind(cmd);
            return cmd.ExecuteScalar() is T value ? value : throw new MaintenanceDataException("لم تصل نسخة الاعتماد. حدّث السجل قبل إعادة المحاولة.");
        }
        catch (SqlException ex) when (ex.Number == 229 && procedure == "dbo.ReopenMaintenanceTicket")
        {
            throw new MaintenanceDataException("إعادة الفتح تتطلب اتصال مشرف مخوّل مستقلًا عن حساب الكاشير.", ex);
        }
        catch (Exception ex) { throw Describe(ex, "تعذّر حفظ إجراء الصيانة."); }
    }

    private static void AddTenders(SqlCommand cmd, decimal cash, decimal vodafone, decimal instaPay)
    {
        AddMoney(cmd, "@Cash", cash); AddMoney(cmd, "@Vodafone", vodafone); AddMoney(cmd, "@InstaPay", instaPay);
    }

    /// <summary>
    /// Loads maintenance orders (newest first) with their parts, activities and payments.
    /// Four set-based queries are used for the whole list (orders + receiver name, part lines,
    /// activity log with actor names, payment history with account kinds) instead of one round
    /// trip per order, so the invoices register stays responsive as the workshop history grows.
    /// </summary>
    public void LoadMaintenanceOrdersInto(ObservableCollection<MaintenanceOrderDesignItem> target)
    {
        var loaded = ReadTickets(null);
        target.Clear();
        foreach (var item in loaded) target.Add(item);
    }

    public MaintenanceOrderDesignItem LoadTicket(int id) => ReadTickets(id).SingleOrDefault()
        ?? throw new MaintenanceDataException("لم يعد أمر الصيانة موجودًا. حدّث القائمة.");

    private List<MaintenanceOrderDesignItem> ReadTickets(int? id)
    {
        try
        {
            using var connection = OpenConnection();
            using var tx = connection.BeginTransaction(IsolationLevel.Serializable);
            var loaded = ReadOrders(connection, tx, id);
            if (loaded.Count > 0)
            {
                ReadOrderParts(connection, tx, id, loaded);
                ReadOrderActivities(connection, tx, id, loaded);
                ReadOrderPayments(connection, tx, id, loaded);
            }
            tx.Commit();
            return loaded;
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر تحميل أوامر الصيانة من قاعدة البيانات.");
        }
    }

    private List<MaintenanceOrderDesignItem> ReadOrders(SqlConnection connection, SqlTransaction tx, int? id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.Parameters.Add("@Id", SqlDbType.Int).Value = (object?)id ?? DBNull.Value;
        command.CommandText = @"
SELECT mo.Id, mo.OrderNumber, mo.CustomerId, mo.CustomerNameSnapshot, mo.CustomerPhoneSnapshot,
 mo.CustomerSecondaryPhoneSnapshot, mo.CustomerAddressSnapshot, mo.IsWalkInCustomer, mo.ReceivedAtUtc,
 ISNULL(NULLIF(LTRIM(RTRIM(u.DisplayName)), N''), N'') AS ReceivedByName,
 mo.OrderStatus, mo.Priority, mo.AssignedTechnician, mo.ExpectedDeliveryAtUtc, mo.ExpectedDeliveryLabel,
 mo.DeviceType, mo.DeviceBrand, mo.DeviceModel, mo.DeviceColor, mo.SerialOrImei, mo.PasscodeType,
 mo.PasscodeValueProtected, mo.ReportedFault, mo.PhysicalCondition, mo.AccessoriesReceived, mo.IntakeNotes,
 mo.IsEstimatePendingInspection, mo.EstimatedCost, mo.DiagnosisNotes, mo.LaborCost, mo.DiscountAmount,
 mo.FinalServiceCost, mo.HasWorkshopPricing, mo.DeliveredAtUtc, mo.WarrantyDays, mo.DeliveryNotes,
 mo.CancellationReason, mo.RowVersion, CASE WHEN IS_ROLEMEMBER(N'ProjectZMaintenanceSupervisor')=1 AND USER_NAME()<>N'ProjectZClient'
 AND HAS_PERMS_BY_NAME(N'dbo.ReopenMaintenanceTicket',N'OBJECT',N'EXECUTE')=1 THEN 1 ELSE 0 END
FROM dbo.MaintenanceOrders AS mo
LEFT JOIN dbo.Users AS u ON u.Id = mo.ReceivedByUserId
WHERE @Id IS NULL OR mo.Id=@Id ORDER BY mo.ReceivedAtUtc DESC, mo.Id DESC OPTION (RECOMPILE);";

        var loaded = new List<MaintenanceOrderDesignItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            loaded.Add(new MaintenanceOrderDesignItem
            {
                Id = reader.GetInt32(0),
                OrderNumber = reader.GetString(1),
                CustomerId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                CustomerName = reader.GetString(3),
                CustomerPhone = reader.GetString(4),
                CustomerSecondaryPhone = reader.GetString(5),
                CustomerAddress = reader.GetString(6),
                IsWalkInCustomer = reader.GetBoolean(7),
                ReceivedAt = reader.GetDateTime(8).ToLocalTime(),
                ReceivedByName = reader.GetString(9),
                Status = (MaintenanceOrderStatus)reader.GetByte(10),
                Priority = (MaintenancePriority)reader.GetByte(11),
                AssignedTechnician = reader.GetString(12),
                ExpectedDeliveryAt = reader.IsDBNull(13) ? null : reader.GetDateTime(13).ToLocalTime(),
                ExpectedDeliveryLabel = reader.GetString(14),
                DeviceType = reader.GetString(15),
                DeviceBrand = reader.GetString(16),
                DeviceModel = reader.GetString(17),
                DeviceColor = reader.GetString(18),
                SerialOrImei = reader.GetString(19),
                PasscodeType = (MaintenancePasscodeType)reader.GetByte(20),
                PasscodeValue = passcodeProtector.Unprotect(reader.GetString(21)),
                ReportedFault = reader.GetString(22),
                PhysicalCondition = reader.GetString(23),
                AccessoriesReceived = reader.GetString(24),
                IntakeNotes = reader.GetString(25),
                IsEstimatePendingInspection = reader.GetBoolean(26),
                EstimatedCost = reader.GetDecimal(27),
                DiagnosisNotes = reader.GetString(28),
                LaborCost = reader.GetDecimal(29),
                DiscountAmount = reader.GetDecimal(30),
                FinalServiceCost = reader.IsDBNull(31) ? null : reader.GetDecimal(31),
                HasWorkshopPricing = reader.GetBoolean(32),
                DeliveredAt = reader.IsDBNull(33) ? null : reader.GetDateTime(33).ToLocalTime(),
                WarrantyDays = reader.GetInt32(34),
                DeliveryNotes = reader.GetString(35),
                CancellationReason = reader.GetString(36),
                HasReopenPermission = reader.GetInt32(38) == 1,
                DatabaseRowVersion = reader.IsDBNull(37) ? null : (byte[])reader[37]
            });
        }

        return loaded;
    }

    /// <summary>Loads every part line of the given orders in one query (ordered by order and line).</summary>
    private static void ReadOrderParts(SqlConnection connection, SqlTransaction tx, int? id, List<MaintenanceOrderDesignItem> orders)
    {
        var ordersById = orders.ToDictionary(o => o.Id);

        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.Parameters.Add("@Id", SqlDbType.Int).Value = (object?)id ?? DBNull.Value;
        command.CommandText = @"
SELECT MaintenanceOrderId, ProductId, SparePartId, BarcodeSnapshot, ProductNameSnapshot, Quantity,
 UnitCost, UnitSellPrice, IsManualPart, Id
FROM dbo.MaintenanceOrderParts
WHERE @Id IS NULL OR MaintenanceOrderId=@Id ORDER BY MaintenanceOrderId, LineNumber OPTION (RECOMPILE);";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!ordersById.TryGetValue(reader.GetInt32(0), out var order)) continue;

            order.UsedParts.Add(new MaintenancePartUsageItem
            {
                LineId = reader.GetInt32(9),
                ProductId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                SparePartId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                Barcode = reader.GetString(3),
                ProductName = reader.GetString(4),
                Quantity = reader.GetInt32(5),
                UnitCost = reader.GetDecimal(6),
                UnitSellPrice = reader.GetDecimal(7),
                IsManualPart = reader.GetBoolean(8)
            });
        }
    }

    /// <summary>Loads the activity log of the given orders in one query, with the actor's display name.</summary>
    private static void ReadOrderActivities(SqlConnection connection, SqlTransaction tx, int? id, List<MaintenanceOrderDesignItem> orders)
    {
        var ordersById = orders.ToDictionary(o => o.Id);

        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.Parameters.Add("@Id", SqlDbType.Int).Value = (object?)id ?? DBNull.Value;
        command.CommandText = @"
SELECT ma.MaintenanceOrderId, ma.OccurredAtUtc, ma.ActionTitle, ma.Details, ma.Amount,
 ISNULL(NULLIF(LTRIM(RTRIM(u.DisplayName)), N''), N'') AS ActorName
FROM dbo.MaintenanceActivities AS ma
LEFT JOIN dbo.Users AS u ON u.Id = ma.ActorUserId
WHERE @Id IS NULL OR ma.MaintenanceOrderId=@Id ORDER BY ma.MaintenanceOrderId, ma.OccurredAtUtc, ma.Id OPTION (RECOMPILE);";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!ordersById.TryGetValue(reader.GetInt32(0), out var order)) continue;

            order.ActivityLog.Add(new MaintenanceActivityEntry
            {
                Timestamp = reader.GetDateTime(1).ToLocalTime(),
                ActionTitle = reader.GetString(2),
                Details = reader.GetString(3),
                Amount = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                ActorName = reader.GetString(5)
            });
        }
    }

    /// <summary>
    /// Loads the payment rows of the given orders in one query and rebuilds both the per-channel
    /// totals the invoice list shows and the payment history the details window lists.
    /// </summary>
    private static void ReadOrderPayments(SqlConnection connection, SqlTransaction tx, int? id, List<MaintenanceOrderDesignItem> orders)
    {
        var ordersById = orders.ToDictionary(o => o.Id);

        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.Parameters.Add("@Id", SqlDbType.Int).Value = (object?)id ?? DBNull.Value;
        command.CommandText = @"
SELECT mp.MaintenanceOrderId, mp.PaymentStage, mp.PaymentKind, mp.Amount, mp.PaidAtUtc, ISNULL(mp.Note, N''),
 fa.AccountKind, ISNULL(NULLIF(LTRIM(RTRIM(u.DisplayName)), N''), N'') AS ActorName, mp.Id,mp.FinancialAccountId,fa.Alias,mp.RefundOfPaymentId,original.PaymentStage
FROM dbo.MaintenancePayments AS mp
LEFT JOIN dbo.MaintenancePayments original ON original.Id=mp.RefundOfPaymentId
INNER JOIN dbo.FinancialAccounts AS fa ON fa.Id = mp.FinancialAccountId
LEFT JOIN dbo.Users AS u ON u.Id = mp.ActorUserId
WHERE @Id IS NULL OR mp.MaintenanceOrderId=@Id ORDER BY mp.MaintenanceOrderId, mp.PaidAtUtc DESC, mp.Id DESC OPTION (RECOMPILE);";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!ordersById.TryGetValue(reader.GetInt32(0), out var order)) continue;

            var stage = reader.GetByte(1);
            var kind = reader.GetByte(2);
            var amount = reader.GetDecimal(3);
            var accountKind = (FinancialAccountKind)reader.GetByte(6);

            order.PaymentHistory.Add(new MaintenancePaymentHistoryItem
            {
                PaidAt = reader.GetDateTime(4).ToLocalTime(),
                PaymentId = reader.GetInt32(8), AccountId = reader.GetInt32(9), AccountAlias = reader.GetString(10),
                RefundOfPaymentId = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                OriginalStage = reader.IsDBNull(12) ? null : reader.GetByte(12),
                Stage = stage,
                Kind = kind,
                Amount = amount,
                Note = reader.GetString(5),
                Account = accountKind,
                ActorName = reader.GetString(7)
            });

            if (kind == 2)
            {
                // A refund (deposit or final) reduces the collected totals.
                order.RefundedDepositAmount += amount;
                continue;
            }

            // Collected payment: deposit (stage 1) or final settlement (stage 2).
            if (stage == 1)
            {
                switch (accountKind)
                {
                    case FinancialAccountKind.Cash:
                        order.DepositCashPaid += amount;
                        break;
                    case FinancialAccountKind.VodafoneCash:
                        order.DepositVodafonePaid += amount;
                        break;
                    case FinancialAccountKind.InstaPay:
                        order.DepositInstaPayPaid += amount;
                        break;
                }
            }
            else
            {
                switch (accountKind)
                {
                    case FinancialAccountKind.Cash:
                        order.FinalPaymentCashPaid += amount;
                        break;
                    case FinancialAccountKind.VodafoneCash:
                        order.FinalPaymentVodafonePaid += amount;
                        break;
                    case FinancialAccountKind.InstaPay:
                        order.FinalPaymentInstaPayPaid += amount;
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Technicians actually present in the database: distinct names already assigned on orders
    /// plus the active user accounts. The invoices screen uses this list for its technician filter
    /// so the filter never offers a name that cannot exist in SQL.
    /// </summary>
    public List<string> LoadTechnicianDirectory()
    {
        var names = new List<string>();
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT DISTINCT TechnicianName FROM
(
    SELECT LTRIM(RTRIM(AssignedTechnician)) AS TechnicianName
    FROM dbo.MaintenanceOrders
    UNION
    SELECT LTRIM(RTRIM(DisplayName)) AS TechnicianName
    FROM dbo.Users
    WHERE IsActive = 1
) AS names
WHERE TechnicianName <> N'';";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
            }
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر تحميل قائمة الفنيين من قاعدة البيانات.");
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    // ---------------------------------------------------------------- helpers

    private static SqlCommand CreateCommand(SqlConnection connection, SqlTransaction transaction, string sql) =>
        new(sql, connection, transaction) { CommandTimeout = 30 };

    private static void AddMoney(SqlCommand command, string name, decimal value)
    {
        if (value != decimal.Round(value, 2)) throw new MaintenanceDataException("أدخل المبالغ بالجنيه والقرش فقط.");
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
                2601 or 2627 => new MaintenanceDataException(
                    "البيانات مكررة: اسم قطعة الغيار أو باركودها مسجل بالفعل لقطعة أخرى.", sql),
                2812 => new MaintenanceDataException("إجراءات الصيانة غير موجودة. شغّل sql/24_EnableMaintenanceSettlement.sql على الخادم.", sql),
                207 or 208 => new MaintenanceDataException(
                    "مخطط قاعدة البيانات ناقص (جدول أو عمود غير موجود). شغّل sql/06_CreateFullSchema.sql ثم sql/13_EnableMaintenanceInvoices.sql وsql/16_EnableMaintenanceSpareParts.sql ثم sql/24_EnableMaintenanceSettlement.sql على الخادم وأعد المحاولة.", sql),
                53 or 4060 => new MaintenanceDataException("لا يمكن الاتصال بقاعدة البيانات. تحقق من اتصال الخادم وتحديثه.", sql),
                18456 => new MaintenanceDataException("فشل تسجيل الدخول إلى قاعدة البيانات. تحقق من المستخدم وكلمة المرور.", sql),
                547 => new MaintenanceDataException(
                    "تعذّر حفظ البيانات بسبب ارتباط غير صالح (مورد أو أمر صيانة أو قيد بنود قديم). شغّل sql/13_EnableMaintenanceInvoices.sql وsql/16_EnableMaintenanceSpareParts.sql ثم sql/24_EnableMaintenanceSettlement.sql على الخادم ثم أعد المحاولة.", sql),
                1205 => new MaintenanceDataException("تعارضت العملية مع مستخدم آخر. حدّث البيانات ثم أعد المحاولة.", sql),
                229 or 230 => new MaintenanceDataException(
                    "حساب الاتصال لا يملك صلاحية العمل على مخزون قطع الغيار. شغّل sql/09_EnablePosSales.sql وsql/16_EnableMaintenanceSpareParts.sql ثم sql/24_EnableMaintenanceSettlement.sql على الخادم ثم أعد المحاولة.", sql),
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
        // The spare-part register has filtered unique indexes (active names/barcodes). SQL Server
        // requires these session options for every INSERT/UPDATE that touches them.
        using (var options = connection.CreateCommand())
        {
            options.CommandText = @"
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;";
            options.ExecuteNonQuery();
        }
        return connection;
    }
}
