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

/// <summary>Result of creating a maintenance order.</summary>
public sealed record SavedMaintenanceOrder(int OrderId, string OrderNumber, decimal TotalDepositPaid);

/// <summary>SQL Server persistence for the maintenance spare-parts inventory and order-part lines.</summary>
/// <summary>A payment to persist for a maintenance order (deposit / final / refund).</summary>
/// <param name="Account">Financial account kind the payment goes through.</param>
/// <param name="Stage">1 deposit, 2 final.</param>
/// <param name="Kind">1 collected, 2 refunded.</param>
public sealed record MaintenancePaymentEntry(
    FinancialAccountKind Account,
    int Stage,
    int Kind,
    decimal Amount,
    DateTime PaidAt,
    string Note);

public sealed class MaintenanceRepository(ConnectionSettingsProvider settings, int actorUserId)
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
                // A spare-part line links dbo.MaintenanceOrderParts.SparePartId and keeps ProductId
                // NULL (the POS catalog is a different table). See sql/13_EnableMaintenanceInvoices.sql.
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

    // ---------------------------------------------------------------- maintenance orders

    /// <summary>
    /// Creates a maintenance order (intake) with its activity log and deposit payments in one
    /// transaction. The order number is taken from DocumentSequences; the deposit payments are
    /// written to MaintenancePayments against the cash / Vodafone Cash / InstaPay accounts.
    /// </summary>
    public SavedMaintenanceOrder CreateMaintenanceOrder(MaintenanceOrderDesignItem order)
    {
        var orderNumber = "";
        decimal totalDeposit = order.DepositCashPaid + order.DepositVodafonePaid + order.DepositInstaPayPaid;

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            orderNumber = NextDocumentNumber(connection, transaction, "MaintenanceOrder", DateTime.Now.Year, "MNT");

            int orderId;
            using (var insert = CreateCommand(connection, transaction, @"
INSERT dbo.MaintenanceOrders(OrderNumber, CustomerId, CustomerNameSnapshot, CustomerPhoneSnapshot,
 CustomerSecondaryPhoneSnapshot, CustomerAddressSnapshot, IsWalkInCustomer, ReceivedAtUtc, ReceivedByUserId,
 OrderStatus, Priority, AssignedTechnician, ExpectedDeliveryAtUtc, ExpectedDeliveryLabel, DeviceType, DeviceBrand,
 DeviceModel, DeviceColor, SerialOrImei, PasscodeType, PasscodeValueProtected, ReportedFault, PhysicalCondition,
 AccessoriesReceived, IntakeNotes, IsEstimatePendingInspection, EstimatedCost)
OUTPUT inserted.Id
VALUES(@Num, @CustomerId, @Name, @Phone, @Phone2, @Address, @WalkIn, @Received, @Actor,
 0, @Priority, @Tech, @Expected, @ExpectedLabel, @DeviceType, @Brand, @Model, @Color, @Serial,
 @PasscodeType, @Passcode, @Fault, @Physical, @Accessories, @Intake, @EstimatePending, @EstimatedCost);"))
            {
                insert.Parameters.Add("@Num", SqlDbType.NVarChar, 50).Value = orderNumber;
                insert.Parameters.Add("@CustomerId", SqlDbType.Int).Value = order.IsWalkInCustomer ? DBNull.Value : (object)order.CustomerId;
                insert.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value = order.CustomerName;
                insert.Parameters.Add("@Phone", SqlDbType.NVarChar, 30).Value = order.CustomerPhone;
                insert.Parameters.Add("@Phone2", SqlDbType.NVarChar, 30).Value = order.CustomerSecondaryPhone;
                insert.Parameters.Add("@Address", SqlDbType.NVarChar, 500).Value = order.CustomerAddress;
                insert.Parameters.Add("@WalkIn", SqlDbType.Bit).Value = order.IsWalkInCustomer;
                insert.Parameters.Add("@Received", SqlDbType.DateTime2).Value = order.ReceivedAt.ToUniversalTime();
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
                orderId = (int)insert.ExecuteScalar()!;
            }

            foreach (var activity in order.ActivityLog)
            {
                using var act = CreateCommand(connection, transaction, @"
INSERT dbo.MaintenanceActivities(MaintenanceOrderId, OccurredAtUtc, ActionTitle, Details, Amount, ActorUserId)
VALUES(@Order,@Time,@Title,@Details,@Amount,@Actor);");
                act.Parameters.Add("@Order", SqlDbType.Int).Value = orderId;
                act.Parameters.Add("@Time", SqlDbType.DateTime2).Value = activity.Timestamp.ToUniversalTime();
                act.Parameters.Add("@Title", SqlDbType.NVarChar, 200).Value = activity.ActionTitle;
                act.Parameters.Add("@Details", SqlDbType.NVarChar, 1500).Value = activity.Details;
                act.Parameters.Add("@Amount", SqlDbType.Decimal).Value = activity.Amount ?? 0m;
                act.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
                act.ExecuteNonQuery();
            }

            if (totalDeposit > 0m)
            {
                var accounts = EnsureAccounts(connection, transaction);
                if (order.DepositCashPaid > 0m)
                    RecordMaintenancePayment(connection, transaction, orderId, orderNumber, order.CustomerName, FinancialAccountKind.Cash, accounts[FinancialAccountKind.Cash], 1, 1, order.DepositCashPaid, order.ReceivedAt, "عربون نقدي");
                if (order.DepositVodafonePaid > 0m)
                    RecordMaintenancePayment(connection, transaction, orderId, orderNumber, order.CustomerName, FinancialAccountKind.VodafoneCash, accounts[FinancialAccountKind.VodafoneCash], 1, 1, order.DepositVodafonePaid, order.ReceivedAt, "عربون فودافون كاش");
                if (order.DepositInstaPayPaid > 0m)
                    RecordMaintenancePayment(connection, transaction, orderId, orderNumber, order.CustomerName, FinancialAccountKind.InstaPay, accounts[FinancialAccountKind.InstaPay], 1, 1, order.DepositInstaPayPaid, order.ReceivedAt, "عربون إنستاباي");
            }

            transaction.Commit();
            return new SavedMaintenanceOrder(orderId, orderNumber, totalDeposit);
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
        var year = DateTime.Now.Year;
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT ISNULL((SELECT LastValue FROM dbo.DocumentSequences
               WHERE DocumentType = @Type AND SequenceYear = @Year), 0) + 1;";
            command.Parameters.Add("@Type", SqlDbType.NVarChar, 30).Value = "MaintenanceOrder";
            command.Parameters.Add("@Year", SqlDbType.SmallInt).Value = year;
            var next = Convert.ToInt64(command.ExecuteScalar() ?? 1L);
            return $"MNT-{year:0000}-{next:0000}";
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
    /// columns and without re-syncing its part lines, unlike <see cref="UpdateMaintenanceOrder"/>.
    /// </summary>
    public void AppendMaintenanceActivity(int orderId, MaintenanceActivityEntry activity)
    {
        if (orderId <= 0) return;

        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT dbo.MaintenanceActivities(MaintenanceOrderId, OccurredAtUtc, ActionTitle, Details, Amount, ActorUserId)
VALUES(@Order,@Time,@Title,@Details,@Amount,@Actor);";
            command.Parameters.Add("@Order", SqlDbType.Int).Value = orderId;
            command.Parameters.Add("@Time", SqlDbType.DateTime2).Value = activity.Timestamp.ToUniversalTime();
            command.Parameters.Add("@Title", SqlDbType.NVarChar, 200).Value = activity.ActionTitle;
            command.Parameters.Add("@Details", SqlDbType.NVarChar, 1500).Value = activity.Details;
            AddMoney(command, "@Amount", activity.Amount ?? 0m);
            command.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر حفظ سجل النشاط في قاعدة البيانات.");
        }
    }

    /// <summary>
    /// Saves the current state of a maintenance order: the editable order columns, any new
    /// activity entries, new payments (deposit / final / refund) and a full re-sync of the
    /// order part lines. All of it runs in one transaction.
    /// </summary>
    /// <param name="order">The order after applying the changes made in the UI.</param>
    /// <param name="newActivities">Activity entries added since the last save; they are also
    /// kept in <c>order.ActivityLog</c> for the in-memory display.</param>
    /// <param name="newPayments">Payments to record in dbo.MaintenancePayments.</param>
    public void UpdateMaintenanceOrder(
        MaintenanceOrderDesignItem order,
        IReadOnlyList<MaintenanceActivityEntry> newActivities,
        IReadOnlyList<MaintenancePaymentEntry> newPayments)
    {
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            using (var update = CreateCommand(connection, transaction, @"
UPDATE dbo.MaintenanceOrders SET
     OrderStatus = @Status, Priority = @Priority, AssignedTechnician = @Tech,
     ExpectedDeliveryAtUtc = @Expected, ExpectedDeliveryLabel = @ExpectedLabel,
     DiagnosisNotes = @Diagnosis, LaborCost = @Labor, DiscountAmount = @Discount,
     EstimatedCost = @Estimated, FinalServiceCost = @FinalCost, HasWorkshopPricing = @HasPricing,
     DeliveredAtUtc = @Delivered, WarrantyDays = @Warranty,
     DeliveryNotes = @DeliveryNotes, CancellationReason = @CancelReason,
     UpdatedAtUtc = SYSUTCDATETIME()
WHERE Id = @Id;"))
            {
                update.Parameters.Add("@Status", SqlDbType.TinyInt).Value = (byte)order.Status;
                update.Parameters.Add("@Priority", SqlDbType.TinyInt).Value = (byte)order.Priority;
                update.Parameters.Add("@Tech", SqlDbType.NVarChar, 150).Value = order.AssignedTechnician;
                update.Parameters.Add("@Expected", SqlDbType.DateTime2).Value = (object?)order.ExpectedDeliveryAt?.ToUniversalTime() ?? DBNull.Value;
                update.Parameters.Add("@ExpectedLabel", SqlDbType.NVarChar, 200).Value = order.ExpectedDeliveryLabel;
                update.Parameters.Add("@Diagnosis", SqlDbType.NVarChar, 2000).Value = order.DiagnosisNotes;
                AddMoney(update, "@Labor", order.LaborCost);
                AddMoney(update, "@Discount", order.DiscountAmount);
                AddMoney(update, "@Estimated", order.EstimatedCost);
                var finalCost = update.Parameters.Add("@FinalCost", SqlDbType.Decimal);
                finalCost.Precision = 19;
                finalCost.Scale = 2;
                finalCost.Value = (object?)order.FinalServiceCost ?? DBNull.Value;
                update.Parameters.Add("@HasPricing", SqlDbType.Bit).Value = order.HasWorkshopPricing;
                update.Parameters.Add("@Delivered", SqlDbType.DateTime2).Value = (object?)order.DeliveredAt?.ToUniversalTime() ?? DBNull.Value;
                update.Parameters.Add("@Warranty", SqlDbType.Int).Value = order.WarrantyDays;
                update.Parameters.Add("@DeliveryNotes", SqlDbType.NVarChar, 1500).Value = order.DeliveryNotes;
                update.Parameters.Add("@CancelReason", SqlDbType.NVarChar, 1500).Value = order.CancellationReason;
                update.Parameters.Add("@Id", SqlDbType.Int).Value = order.Id;
                if (update.ExecuteNonQuery() == 0)
                    throw new MaintenanceDataException("تعذّر تحديث أمر الصيانة: غير موجود في قاعدة البيانات.");
            }

            foreach (var activity in newActivities)
                InsertActivity(connection, transaction, order.Id, activity);

            if (newPayments.Count > 0)
            {
                var accounts = EnsureAccounts(connection, transaction);
                foreach (var payment in newPayments)
                {
                    if (!accounts.TryGetValue(payment.Account, out var accountId))
                        throw new MaintenanceDataException("تعذّر تسجيل الدفعة: الحساب المالي غير متوفر.");
                    RecordMaintenancePayment(
                        connection, transaction, order.Id, order.OrderNumber, order.CustomerName, payment.Account, accountId,
                        payment.Stage, payment.Kind, payment.Amount, payment.PaidAt, payment.Note);
                }
            }

            SaveOrderParts(connection, transaction, order);

            transaction.Commit();
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر حفظ تحديثات أمر الصيانة.");
        }
    }

    private void InsertActivity(
        SqlConnection connection, SqlTransaction transaction, int orderId, MaintenanceActivityEntry entry)
    {
        using var act = CreateCommand(connection, transaction, @"
INSERT dbo.MaintenanceActivities(MaintenanceOrderId, OccurredAtUtc, ActionTitle, Details, Amount, ActorUserId)
VALUES(@Order,@Time,@Title,@Details,@Amount,@Actor);");
        act.Parameters.Add("@Order", SqlDbType.Int).Value = orderId;
        act.Parameters.Add("@Time", SqlDbType.DateTime2).Value = entry.Timestamp.ToUniversalTime();
        act.Parameters.Add("@Title", SqlDbType.NVarChar, 200).Value = entry.ActionTitle;
        act.Parameters.Add("@Details", SqlDbType.NVarChar, 1500).Value = entry.Details;
        act.Parameters.Add("@Amount", SqlDbType.Decimal).Value = entry.Amount ?? 0m;
        act.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
        act.ExecuteNonQuery();
    }

    /// <summary>
    /// Records a maintenance payment in three places in one step: the MaintenancePayments row,
    /// the FinancialAccounts balance (+ collected / - refunded) and the FinancialTransactions
    /// ledger row that the financial screens read. The ledger row links back to the order via
    /// SourceEntityType/SourceEntityId so payments reconcile with the rest of the program.
    /// </summary>
    private void RecordMaintenancePayment(
        SqlConnection connection, SqlTransaction transaction, int orderId, string orderNumber,
        string customerName, FinancialAccountKind accountKind, int accountId, int stage, int kind,
        decimal amount, DateTime paidAt, string note)
    {
        // 1) MaintenancePayments row.
        using (var pay = CreateCommand(connection, transaction, @"
INSERT dbo.MaintenancePayments(MaintenanceOrderId, FinancialAccountId, PaymentStage, PaymentKind, Amount, PaidAtUtc, Note, ActorUserId)
VALUES(@Order,@Account,@Stage,@Kind,@Amount,@Paid,@Note,@Actor);"))
        {
            pay.Parameters.Add("@Order", SqlDbType.Int).Value = orderId;
            pay.Parameters.Add("@Account", SqlDbType.Int).Value = accountId;
            pay.Parameters.Add("@Stage", SqlDbType.TinyInt).Value = stage;
            pay.Parameters.Add("@Kind", SqlDbType.TinyInt).Value = kind;
            AddMoney(pay, "@Amount", amount);
            pay.Parameters.Add("@Paid", SqlDbType.DateTime2).Value = paidAt.ToUniversalTime();
            pay.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value = note;
            pay.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
            pay.ExecuteNonQuery();
        }

        // 2) Update the account balance and capture the resulting balance.
        var isIncoming = kind == 1;
        decimal balanceAfter;
        using (var balance = CreateCommand(connection, transaction, isIncoming
            ? @"UPDATE dbo.FinancialAccounts SET CurrentBalance = CurrentBalance + @Amount, UpdatedAtUtc = SYSUTCDATETIME()
OUTPUT inserted.CurrentBalance WHERE Id = @Id;"
            : @"UPDATE dbo.FinancialAccounts SET CurrentBalance = CurrentBalance - @Amount, UpdatedAtUtc = SYSUTCDATETIME()
OUTPUT inserted.CurrentBalance WHERE Id = @Id;"))
        {
            AddMoney(balance, "@Amount", amount);
            balance.Parameters.Add("@Id", SqlDbType.Int).Value = accountId;
            balanceAfter = (decimal)balance.ExecuteScalar()!;
        }

        // 3) FinancialTransactions ledger row (read by My Accounts / records screens).
        using var ledger = CreateCommand(connection, transaction, @"
INSERT dbo.FinancialTransactions(FinancialAccountId,OccurredAtUtc,IsIncoming,Category,Amount,BalanceAfter,Counterparty,Note,
 ActorUserId,CountsTowardLimits,SourceEntityType,SourceEntityId)
VALUES(@Account,SYSUTCDATETIME(),@Incoming,@Category,@Amount,@Balance,@Counterparty,@Note,@Actor,@Limits,N'MaintenanceOrder',@Order);");
        ledger.Parameters.Add("@Account", SqlDbType.Int).Value = accountId;
        ledger.Parameters.Add("@Incoming", SqlDbType.Bit).Value = isIncoming;
        ledger.Parameters.Add("@Category", SqlDbType.NVarChar, 150).Value = isIncoming ? "تحصيل صيانة" : "رد دفعة صيانة";
        AddMoney(ledger, "@Amount", amount);
        AddMoney(ledger, "@Balance", balanceAfter);
        ledger.Parameters.Add("@Counterparty", SqlDbType.NVarChar, 250).Value =
            Trim(string.IsNullOrWhiteSpace(customerName) ? "عميل نقدي" : customerName, 250, "عميل نقدي");
        ledger.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value =
            Trim(string.IsNullOrWhiteSpace(orderNumber) ? note : $"{orderNumber} · {note}", 1000, note);
        ledger.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
        ledger.Parameters.Add("@Limits", SqlDbType.Bit).Value = accountKind != FinancialAccountKind.Cash;
        ledger.Parameters.Add("@Order", SqlDbType.Int).Value = orderId;
        ledger.ExecuteNonQuery();
    }

    /// <summary>Full re-sync of the order part lines (used parts) from the in-memory collection.</summary>
    private static void SaveOrderParts(SqlConnection connection, SqlTransaction transaction, MaintenanceOrderDesignItem order)
    {
        using (var delete = CreateCommand(connection, transaction, @"
DELETE FROM dbo.MaintenanceOrderParts WHERE MaintenanceOrderId = @Order;"))
        {
            delete.Parameters.Add("@Order", SqlDbType.Int).Value = order.Id;
            delete.ExecuteNonQuery();
        }

        var line = 0;
        foreach (var part in order.UsedParts)
        {
            line++;

            // Manual lines have neither id. Spare-part lines carry MaintenanceSpareParts.Id in
            // SparePartId (legacy rows may still hold it in ProductId, hence EffectiveSparePartId)
            // and must keep ProductId NULL so the FK to dbo.Products is never misused.
            var sparePartId = part.IsManualPart ? 0 : part.EffectiveSparePartId;

            using var insert = CreateCommand(connection, transaction, @"
INSERT dbo.MaintenanceOrderParts(MaintenanceOrderId, SparePartId, ProductId, LineNumber, BarcodeSnapshot,
 ProductNameSnapshot, Quantity, UnitCost, UnitSellPrice, IsManualPart)
VALUES(@Order,@Spare,@Product,@Line,@Barcode,@Name,@Qty,@Cost,@Sell,@Manual);");
            insert.Parameters.Add("@Order", SqlDbType.Int).Value = order.Id;
            insert.Parameters.Add("@Spare", SqlDbType.Int).Value =
                sparePartId > 0 ? (object)sparePartId : DBNull.Value;
            // Maintenance never writes POS catalog lines today, so the FK to dbo.Products stays NULL.
            insert.Parameters.Add("@Product", SqlDbType.Int).Value = DBNull.Value;
            insert.Parameters.Add("@Line", SqlDbType.Int).Value = line;
            insert.Parameters.Add("@Barcode", SqlDbType.NVarChar, 100).Value = part.Barcode;
            insert.Parameters.Add("@Name", SqlDbType.NVarChar, 250).Value = part.ProductName;
            insert.Parameters.Add("@Qty", SqlDbType.Int).Value = part.Quantity;
            AddMoney(insert, "@Cost", part.UnitCost);
            AddMoney(insert, "@Sell", part.UnitSellPrice);
            insert.Parameters.Add("@Manual", SqlDbType.Bit).Value = part.IsManualPart;
            insert.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Loads maintenance orders (newest first) with their parts, activities and payments.
    /// Four set-based queries are used for the whole list (orders + receiver name, part lines,
    /// activity log with actor names, payment history with account kinds) instead of one round
    /// trip per order, so the invoices register stays responsive as the workshop history grows.
    /// </summary>
    public void LoadMaintenanceOrdersInto(ObservableCollection<MaintenanceOrderDesignItem> target)
    {
        List<MaintenanceOrderDesignItem> loaded;
        try
        {
            using var connection = OpenConnection();
            loaded = ReadOrders(connection);
            if (loaded.Count > 0)
            {
                ReadOrderParts(connection, loaded);
                ReadOrderActivities(connection, loaded);
                ReadOrderPayments(connection, loaded);
            }
        }
        catch (Exception ex)
        {
            throw Describe(ex, "تعذّر تحميل أوامر الصيانة من قاعدة البيانات.");
        }

        target.Clear();
        foreach (var item in loaded) target.Add(item);
    }

    private List<MaintenanceOrderDesignItem> ReadOrders(SqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT mo.Id, mo.OrderNumber, mo.CustomerId, mo.CustomerNameSnapshot, mo.CustomerPhoneSnapshot,
 mo.CustomerSecondaryPhoneSnapshot, mo.CustomerAddressSnapshot, mo.IsWalkInCustomer, mo.ReceivedAtUtc,
 ISNULL(NULLIF(LTRIM(RTRIM(u.DisplayName)), N''), N'') AS ReceivedByName,
 mo.OrderStatus, mo.Priority, mo.AssignedTechnician, mo.ExpectedDeliveryAtUtc, mo.ExpectedDeliveryLabel,
 mo.DeviceType, mo.DeviceBrand, mo.DeviceModel, mo.DeviceColor, mo.SerialOrImei, mo.PasscodeType,
 mo.PasscodeValueProtected, mo.ReportedFault, mo.PhysicalCondition, mo.AccessoriesReceived, mo.IntakeNotes,
 mo.IsEstimatePendingInspection, mo.EstimatedCost, mo.DiagnosisNotes, mo.LaborCost, mo.DiscountAmount,
 mo.FinalServiceCost, mo.HasWorkshopPricing, mo.DeliveredAtUtc, mo.WarrantyDays, mo.DeliveryNotes,
 mo.CancellationReason
FROM dbo.MaintenanceOrders AS mo
LEFT JOIN dbo.Users AS u ON u.Id = mo.ReceivedByUserId
ORDER BY mo.ReceivedAtUtc DESC, mo.Id DESC;";

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
                CancellationReason = reader.GetString(36)
            });
        }

        return loaded;
    }

    /// <summary>Loads every part line of the given orders in one query (ordered by order and line).</summary>
    private static void ReadOrderParts(SqlConnection connection, List<MaintenanceOrderDesignItem> orders)
    {
        var ordersById = orders.ToDictionary(o => o.Id);

        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT MaintenanceOrderId, ProductId, SparePartId, BarcodeSnapshot, ProductNameSnapshot, Quantity,
 UnitCost, UnitSellPrice, IsManualPart
FROM dbo.MaintenanceOrderParts
ORDER BY MaintenanceOrderId, LineNumber;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!ordersById.TryGetValue(reader.GetInt32(0), out var order)) continue;

            order.UsedParts.Add(new MaintenancePartUsageItem
            {
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
    private static void ReadOrderActivities(SqlConnection connection, List<MaintenanceOrderDesignItem> orders)
    {
        var ordersById = orders.ToDictionary(o => o.Id);

        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT ma.MaintenanceOrderId, ma.OccurredAtUtc, ma.ActionTitle, ma.Details, ma.Amount,
 ISNULL(NULLIF(LTRIM(RTRIM(u.DisplayName)), N''), N'') AS ActorName
FROM dbo.MaintenanceActivities AS ma
LEFT JOIN dbo.Users AS u ON u.Id = ma.ActorUserId
ORDER BY ma.MaintenanceOrderId, ma.OccurredAtUtc, ma.Id;";
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
    private static void ReadOrderPayments(SqlConnection connection, List<MaintenanceOrderDesignItem> orders)
    {
        var ordersById = orders.ToDictionary(o => o.Id);

        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT mp.MaintenanceOrderId, mp.PaymentStage, mp.PaymentKind, mp.Amount, mp.PaidAtUtc, ISNULL(mp.Note, N''),
 fa.AccountKind, ISNULL(NULLIF(LTRIM(RTRIM(u.DisplayName)), N''), N'') AS ActorName
FROM dbo.MaintenancePayments AS mp
INNER JOIN dbo.FinancialAccounts AS fa ON fa.Id = mp.FinancialAccountId
LEFT JOIN dbo.Users AS u ON u.Id = mp.ActorUserId
ORDER BY mp.MaintenanceOrderId, mp.PaidAtUtc DESC, mp.Id DESC;";
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

    private static string NextDocumentNumber(
        SqlConnection connection, SqlTransaction transaction, string documentType, int year, string prefix)
    {
        using var command = CreateCommand(connection, transaction, @"
IF NOT EXISTS (SELECT 1 FROM dbo.DocumentSequences WITH (UPDLOCK, HOLDLOCK)
              WHERE DocumentType = @Type AND SequenceYear = @Year)
    INSERT dbo.DocumentSequences(DocumentType,SequenceYear,LastValue) VALUES(@Type,@Year,0);
UPDATE dbo.DocumentSequences SET LastValue = LastValue + 1
OUTPUT inserted.LastValue
WHERE DocumentType = @Type AND SequenceYear = @Year;");
        command.Parameters.Add("@Type", SqlDbType.NVarChar, 30).Value = documentType;
        command.Parameters.Add("@Year", SqlDbType.SmallInt).Value = year;
        var value = command.ExecuteScalar();
        return $"{prefix}-{year:0000}-{(long)value:0000}";
    }

    private static IReadOnlyDictionary<FinancialAccountKind, int> EnsureAccounts(
        SqlConnection connection, SqlTransaction transaction)
    {
        var accounts = new Dictionary<FinancialAccountKind, int>();

        using (var command = CreateCommand(connection, transaction, @"
SELECT Id, AccountKind FROM dbo.FinancialAccounts WITH (UPDLOCK, HOLDLOCK)
WHERE IsActive = 1
ORDER BY IsDefault DESC, Id;"))
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var kind = (FinancialAccountKind)reader.GetByte(1);
                accounts.TryAdd(kind, reader.GetInt32(0));
            }
        }

        foreach (var kind in new[] { FinancialAccountKind.Cash, FinancialAccountKind.VodafoneCash, FinancialAccountKind.InstaPay })
        {
            if (accounts.ContainsKey(kind)) continue;

            using var insert = CreateCommand(connection, transaction, @"
INSERT dbo.FinancialAccounts(AccountKind,Alias,Identifier,CurrentBalance,IsActive,IsDefault,Notes)
OUTPUT inserted.Id
VALUES(@Kind,@Alias,@Identifier,0,1,1,N'أنشئت تلقائيًا لأوامر الصيانة');");
            insert.Parameters.Add("@Kind", SqlDbType.TinyInt).Value = (byte)kind;
            insert.Parameters.Add("@Alias", SqlDbType.NVarChar, 200).Value = kind switch
            {
                FinancialAccountKind.VodafoneCash => "فودافون كاش",
                FinancialAccountKind.InstaPay => "انستاباي",
                _ => "الخزنة الرئيسية"
            };
            insert.Parameters.Add("@Identifier", SqlDbType.NVarChar, 200).Value = kind switch
            {
                FinancialAccountKind.VodafoneCash => "غير محدد",
                FinancialAccountKind.InstaPay => "غير محدد",
                _ => ""
            };
            accounts[kind] = (int)insert.ExecuteScalar()!;
        }

        return accounts;
    }

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
                207 or 208 => new MaintenanceDataException(
                    "مخطط قاعدة البيانات ناقص (جدول أو عمود غير موجود). شغّل sql/06_CreateFullSchema.sql ثم sql/13_EnableMaintenanceInvoices.sql على الخادم وأعد المحاولة.", sql),
                53 or 4060 => new MaintenanceDataException("لا يمكن الاتصال بقاعدة البيانات. تحقق من اتصال الخادم وتحديثه.", sql),
                18456 => new MaintenanceDataException("فشل تسجيل الدخول إلى قاعدة البيانات. تحقق من المستخدم وكلمة المرور.", sql),
                547 => new MaintenanceDataException(
                    "تعذّر حفظ بند قطعة الغيار: قيد بنود الصيانة في قاعدة البيانات ما زال النسخة القديمة. شغّل sql/13_EnableMaintenanceInvoices.sql على الخادم ثم أعد المحاولة.", sql),
                1205 => new MaintenanceDataException("تعارضت العملية مع مستخدم آخر. حدّث البيانات ثم أعد المحاولة.", sql),
                229 or 230 => new MaintenanceDataException(
                    "حساب الاتصال لا يملك صلاحية العمل على فواتير الصيانة. شغّل sql/09_EnablePosSales.sql وsql/13_EnableMaintenanceInvoices.sql على الخادم ثم أعد المحاولة.", sql),
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
