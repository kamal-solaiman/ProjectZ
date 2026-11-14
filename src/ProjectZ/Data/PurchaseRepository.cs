using System.Collections.ObjectModel;
using System.Data;
using Microsoft.Data.SqlClient;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Data;

/// <summary>User-facing failure raised by the purchasing layer; the message is safe to show in the UI.</summary>
public sealed class PurchaseDataException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

public sealed record PurchaseLineRequest(
    int ProductId,
    string Barcode,
    string ProductName,
    string Category,
    string Unit,
    bool TrackSerialNumber,
    int Quantity,
    decimal UnitPurchaseCost);

public sealed record PurchaseInvoiceRequest(
    int SupplierId,
    string SupplierInvoiceNumber,
    DateTime IssuedAt,
    decimal DiscountAmount,
    IReadOnlyList<PurchaseLineRequest> Lines,
    decimal CashPaid,
    decimal VodafonePaid,
    decimal InstaPayPaid,
    string Notes);

public sealed record SavedPurchase(
    int InvoiceId,
    string InvoiceNumber,
    decimal NetTotal,
    decimal PaidTotal,
    decimal RemainingPayable,
    PurchaseInvoiceStatus Status);

public sealed record PurchasePaymentRequest(
    int InvoiceId,
    decimal CashPaid,
    decimal VodafonePaid,
    decimal InstaPayPaid,
    string Notes);

public sealed record PurchaseReturnLineInput(int LineId, int Quantity);

public sealed record PurchaseReturnRequest(
    int InvoiceId,
    IReadOnlyList<PurchaseReturnLineInput> Lines,
    string Reason,
    string Notes,
    bool PreferDamagedFirst,
    string RefundChannel);

public sealed record PurchaseReturnResult(
    string ReturnNumber,
    int ReturnedUnits,
    decimal ReturnedValue,
    decimal CreditApplied,
    decimal RefundReceived,
    PurchaseInvoiceStatus Status);

public sealed record PurchaseLineEdit(int? LineId, int ProductId, string Barcode, string ProductName,
    string Category, string Unit, bool TrackSerialNumber, int Quantity, decimal UnitPurchaseCost);

public sealed record PurchaseInvoiceEdit(
    int InvoiceId,
    int SupplierId,
    string SupplierInvoiceNumber,
    string Notes,
    decimal DiscountAmount,
    decimal CashPaid,
    decimal VodafonePaid,
    decimal InstaPayPaid,
    IReadOnlyList<PurchaseLineEdit> Lines);

/// <summary>SQL Server persistence for the purchase invoice screens and the supplier directory.</summary>
public sealed class PurchaseRepository(ConnectionSettingsProvider settings, int actorUserId)
{
    private const string InvoiceSequence = "PurchaseInvoice";
    private const string ReturnSequence = "PurchaseReturn";
    private const string AuditModule = "Purchasing";

    // ---------------------------------------------------------------- suppliers

    /// <summary>Reads the active supplier directory; invoice totals are calculated from the purchase ledger.</summary>
    public List<SupplierDesignItem> LoadSuppliers()
    {
        var suppliers = new List<SupplierDesignItem>();

        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT s.Id, s.Name, ISNULL(s.Phone,N''), ISNULL(s.SecondaryPhone,N''), ISNULL(s.CompanyName,N''),
       ISNULL(s.Specialization,N''), ISNULL(s.Address,N''), ISNULL(s.VodafoneCashNumber,N''),
       ISNULL(s.InstaPayAddress,N''), ISNULL(s.Notes,N'')
FROM dbo.Suppliers s
WHERE s.IsActive = 1
ORDER BY s.Name;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                suppliers.Add(new SupplierDesignItem
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Phone = reader.GetString(2),
                    SecondaryPhone = reader.GetString(3),
                    CompanyName = reader.GetString(4),
                    Specialization = reader.GetString(5),
                    Address = reader.GetString(6),
                    VodafoneCashNumber = reader.GetString(7),
                    InstaPayAddress = reader.GetString(8),
                    Notes = reader.GetString(9)
                });
            }
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            throw Describe(ex, "تعذر تحميل دليل الموردين من قاعدة البيانات.");
        }

        return suppliers;
    }

    public void LoadSuppliersInto(ObservableCollection<SupplierDesignItem> target)
    {
        var loaded = LoadSuppliers();
        target.Clear();
        foreach (var supplier in loaded)
            target.Add(supplier);
    }

    /// <summary>Names already registered, used by the add-supplier dialog before it saves.</summary>
    public HashSet<string> LoadSupplierNames()
    {
        var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name FROM dbo.Suppliers;";
        using var reader = command.ExecuteReader();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    public SupplierDesignItem CreateSupplier(
        string name, string phone, string secondaryPhone, string companyName,
        string specialization, string address, string vodafoneCashNumber, string instaPayAddress, string notes)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new PurchaseDataException("يرجى إدخال اسم المورد.");
        if (name.Length > 200) throw new PurchaseDataException("اسم المورد يجب ألا يتجاوز 200 حرف.");

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                using (var existing = CreateCommand(connection, transaction,
                    "SELECT Id FROM dbo.Suppliers WITH (UPDLOCK,HOLDLOCK) WHERE Name=@Name;"))
                {
                    existing.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value = name;
                    if (existing.ExecuteScalar() is not null)
                        throw new PurchaseDataException($"يوجد مورد مسجل بالاسم «{name}» بالفعل. اختره من القائمة أو غيّر الاسم.");
                }

                int id;
                using (var insert = CreateCommand(connection, transaction, @"
INSERT dbo.Suppliers(Name,Phone,SecondaryPhone,CompanyName,Specialization,Address,VodafoneCashNumber,InstaPayAddress,Notes)
OUTPUT inserted.Id
VALUES(@Name,@Phone,@Secondary,@Company,@Specialization,@Address,@Vodafone,@InstaPay,@Notes);"))
                {
                    insert.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value = name;
                    insert.Parameters.Add("@Phone", SqlDbType.NVarChar, 30).Value = Trim(phone, 30, "");
                    insert.Parameters.Add("@Secondary", SqlDbType.NVarChar, 30).Value = Trim(secondaryPhone, 30, "");
                    insert.Parameters.Add("@Company", SqlDbType.NVarChar, 200).Value = Trim(companyName, 200, "");
                    insert.Parameters.Add("@Specialization", SqlDbType.NVarChar, 200).Value = Trim(specialization, 200, "");
                    insert.Parameters.Add("@Address", SqlDbType.NVarChar, 500).Value = Trim(address, 500, "");
                    insert.Parameters.Add("@Vodafone", SqlDbType.NVarChar, 30).Value = Trim(vodafoneCashNumber, 30, "");
                    insert.Parameters.Add("@InstaPay", SqlDbType.NVarChar, 200).Value = Trim(instaPayAddress, 200, "");
                    insert.Parameters.Add("@Notes", SqlDbType.NVarChar, 1000).Value = Trim(notes, 1000, "");
                    id = (int)insert.ExecuteScalar()!;
                }

                Audit(connection, transaction, "CreateSupplier", id, $"إضافة المورد {name}", "Supplier");
                transaction.Commit();

                return new SupplierDesignItem
                {
                    Id = id,
                    Name = name,
                    Phone = Trim(phone, 30, ""),
                    SecondaryPhone = Trim(secondaryPhone, 30, ""),
                    CompanyName = Trim(companyName, 200, ""),
                    Specialization = Trim(specialization, 200, ""),
                    Address = Trim(address, 500, ""),
                    VodafoneCashNumber = Trim(vodafoneCashNumber, 30, ""),
                    InstaPayAddress = Trim(instaPayAddress, 200, ""),
                    Notes = Trim(notes, 1000, "")
                };
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not PurchaseDataException))
        {
            throw Describe(ex, "تعذر حفظ المورد في قاعدة البيانات.");
        }
    }

    /// <summary>Updates the supplier row and re-stamps the invoice snapshots that belong to it.</summary>
    public void UpdateSupplier(SupplierDesignItem supplier)
    {
        if (supplier.Id <= 0) throw new PurchaseDataException("لا يمكن تعديل مورد غير محفوظ في قاعدة البيانات.");

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                using (var command = CreateCommand(connection, transaction, @"
UPDATE dbo.Suppliers
SET Name=@Name, Phone=@Phone, SecondaryPhone=@Secondary, CompanyName=@Company, Specialization=@Specialization,
    Address=@Address, VodafoneCashNumber=@Vodafone, InstaPayAddress=@InstaPay, Notes=@Notes,
    UpdatedAtUtc=SYSUTCDATETIME()
WHERE Id=@Id;"))
                {
                    command.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value = Trim(supplier.Name, 200, "");
                    command.Parameters.Add("@Phone", SqlDbType.NVarChar, 30).Value = Trim(supplier.Phone, 30, "");
                    command.Parameters.Add("@Secondary", SqlDbType.NVarChar, 30).Value = Trim(supplier.SecondaryPhone, 30, "");
                    command.Parameters.Add("@Company", SqlDbType.NVarChar, 200).Value = Trim(supplier.CompanyName, 200, "");
                    command.Parameters.Add("@Specialization", SqlDbType.NVarChar, 200).Value = Trim(supplier.Specialization, 200, "");
                    command.Parameters.Add("@Address", SqlDbType.NVarChar, 500).Value = Trim(supplier.Address, 500, "");
                    command.Parameters.Add("@Vodafone", SqlDbType.NVarChar, 30).Value = Trim(supplier.VodafoneCashNumber, 30, "");
                    command.Parameters.Add("@InstaPay", SqlDbType.NVarChar, 200).Value = Trim(supplier.InstaPayAddress, 200, "");
                    command.Parameters.Add("@Notes", SqlDbType.NVarChar, 1000).Value = Trim(supplier.Notes, 1000, "");
                    command.Parameters.Add("@Id", SqlDbType.Int).Value = supplier.Id;
                    if (command.ExecuteNonQuery() != 1)
                        throw new PurchaseDataException("المورد غير موجود في قاعدة البيانات؛ حدّث شاشة الموردين ثم أعد المحاولة.");
                }

                using (var snapshots = CreateCommand(connection, transaction, @"
UPDATE dbo.PurchaseInvoices
SET SupplierNameSnapshot=@Name, SupplierPhoneSnapshot=@Phone, SupplierCompanySnapshot=@Company,
    SupplierAddressSnapshot=@Address, UpdatedAtUtc=SYSUTCDATETIME()
WHERE SupplierId=@Id;"))
                {
                    snapshots.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value = Trim(supplier.Name, 200, "");
                    snapshots.Parameters.Add("@Phone", SqlDbType.NVarChar, 30).Value = Trim(supplier.Phone, 30, "");
                    snapshots.Parameters.Add("@Company", SqlDbType.NVarChar, 200).Value = Trim(supplier.CompanyName, 200, "");
                    snapshots.Parameters.Add("@Address", SqlDbType.NVarChar, 500).Value = Trim(supplier.Address, 500, "");
                    snapshots.Parameters.Add("@Id", SqlDbType.Int).Value = supplier.Id;
                    snapshots.ExecuteNonQuery();
                }

                Audit(connection, transaction, "UpdateSupplier", supplier.Id, $"تعديل بيانات المورد {supplier.Name}", "Supplier");
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not PurchaseDataException))
        {
            throw Describe(ex, "تعذر حفظ تعديلات المورد في قاعدة البيانات.");
        }
    }

    /// <returns>True when the row was deleted; false when history required archiving instead.</returns>
    public bool DeleteSupplier(int supplierId)
    {
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                bool hasHistory;
                using (var check = CreateCommand(connection, transaction, @"
SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.PurchaseInvoices WHERE SupplierId=@Id)
              OR EXISTS (SELECT 1 FROM dbo.InventoryUnits WHERE SupplierId=@Id)
              OR EXISTS (SELECT 1 FROM dbo.MaintenanceOrderParts WHERE SupplierId=@Id)
            THEN 1 ELSE 0 END;"))
                {
                    check.Parameters.Add("@Id", SqlDbType.Int).Value = supplierId;
                    hasHistory = Convert.ToInt32(check.ExecuteScalar()) == 1;
                }

                if (hasHistory)
                {
                    using var archive = CreateCommand(connection, transaction,
                        "UPDATE dbo.Suppliers SET IsActive=0, UpdatedAtUtc=SYSUTCDATETIME() WHERE Id=@Id;");
                    archive.Parameters.Add("@Id", SqlDbType.Int).Value = supplierId;
                    archive.ExecuteNonQuery();
                    Audit(connection, transaction, "ArchiveSupplier", supplierId,
                        "أرشفة المورد بعد وجود فواتير أو وحدات مرتبطة", "Supplier");
                    transaction.Commit();
                    return false;
                }

                using (var delete = CreateCommand(connection, transaction, "DELETE dbo.Suppliers WHERE Id=@Id;"))
                {
                    delete.Parameters.Add("@Id", SqlDbType.Int).Value = supplierId;
                    delete.ExecuteNonQuery();
                }

                Audit(connection, transaction, "DeleteSupplier", supplierId, "حذف مورد بدون سجل مشتريات", "Supplier");
                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not PurchaseDataException))
        {
            throw Describe(ex, "تعذر حذف المورد من قاعدة البيانات.");
        }
    }

    // ----------------------------------------------------------------- invoices

    /// <summary>Replaces the shared invoice collection with the purchase ledger read from SQL Server.</summary>
    public void LoadInvoicesInto(ObservableCollection<PurchaseInvoiceDesignItem> target)
    {
        var headers = new List<PurchaseInvoiceDesignItem>();
        var byId = new Dictionary<int, PurchaseInvoiceDesignItem>();
        var lineUnits = new Dictionary<int, List<int>>();
        var lineSerials = new Dictionary<int, List<string>>();
        var lineById = new Dictionary<int, PurchaseInvoiceLineDesignItem>();

        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT i.Id, i.InvoiceNumber, ISNULL(i.SupplierInvoiceNumber,N''), i.SupplierId, i.SupplierNameSnapshot,
       i.SupplierPhoneSnapshot, i.SupplierCompanySnapshot, i.SupplierAddressSnapshot, i.IssuedAtUtc, i.InvoiceStatus,
       i.DiscountAmount, i.Notes, u.DisplayName
FROM dbo.PurchaseInvoices i
JOIN dbo.Users u ON u.Id = i.BuyerUserId
ORDER BY i.IssuedAtUtc DESC, i.Id DESC;
SELECT l.PurchaseInvoiceId, l.Id, l.LineNumber, l.ProductId, ISNULL(l.BarcodeSnapshot,N''), l.ProductNameSnapshot,
       ISNULL(l.CategoryNameSnapshot,N''), l.UnitNameSnapshot, l.TrackSerialNumber, l.Quantity, l.ReturnedQuantity,
       l.UnitPurchaseCost
FROM dbo.PurchaseInvoiceLines l
ORDER BY l.PurchaseInvoiceId, l.LineNumber;
SELECT lu.PurchaseInvoiceLineId, lu.InventoryUnitId, COALESCE(iu.SerialNumber,N'')
FROM dbo.PurchaseInvoiceLineUnits lu
JOIN dbo.InventoryUnits iu ON iu.Id = lu.InventoryUnitId;
SELECT p.PurchaseInvoiceId, a.AccountKind, p.PaymentKind, p.Amount
FROM dbo.PurchasePayments p
JOIN dbo.FinancialAccounts a ON a.Id = p.FinancialAccountId;
SELECT a.PurchaseInvoiceId, a.OccurredAtUtc, a.ActionTitle, a.Details, u.DisplayName, a.Amount
FROM dbo.PurchaseInvoiceActivities a
JOIN dbo.Users u ON u.Id = a.ActorUserId
ORDER BY a.Id;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var invoice = new PurchaseInvoiceDesignItem
                {
                    Id = reader.GetInt32(0),
                    InvoiceNumber = reader.GetString(1),
                    SupplierInvoiceNumber = reader.GetString(2),
                    SupplierId = reader.GetInt32(3),
                    SupplierName = reader.GetString(4),
                    SupplierPhone = reader.GetString(5),
                    SupplierCompany = reader.GetString(6),
                    SupplierAddress = reader.GetString(7),
                    IssuedAt = AsLocal(reader.GetDateTime(8)),
                    BuyerName = reader.GetString(12),
                    DiscountAmount = reader.GetDecimal(10),
                    Notes = reader.GetString(11)
                };
                invoice.Status = (PurchaseInvoiceStatus)reader.GetByte(9);

                headers.Add(invoice);
                byId[invoice.Id] = invoice;
            }

            reader.NextResult();
            while (reader.Read())
            {
                if (!byId.TryGetValue(reader.GetInt32(0), out var invoice)) continue;

                var line = new PurchaseInvoiceLineDesignItem
                {
                    DatabaseLineId = reader.GetInt32(1),
                    LineNumber = reader.GetInt32(2),
                    ProductId = reader.GetInt32(3),
                    Barcode = reader.GetString(4),
                    ProductName = reader.GetString(5),
                    Category = reader.GetString(6),
                    Unit = reader.GetString(7),
                    TrackSerialNumber = reader.GetBoolean(8),
                    Quantity = reader.GetInt32(9),
                    UnitPurchaseCost = reader.GetDecimal(11)
                };

                // ReturnedQuantity clamps against Quantity, so it is assigned after the quantity above.
                var returned = reader.GetInt32(10);
                if (returned > 0) line.ReturnedQuantity = returned;

                invoice.Items.Add(line);
                lineById[line.DatabaseLineId] = line;
                lineUnits[line.DatabaseLineId] = [];
                lineSerials[line.DatabaseLineId] = [];
            }

            reader.NextResult();
            while (reader.Read())
            {
                var lineId = reader.GetInt32(0);
                if (!lineUnits.TryGetValue(lineId, out var units)) continue;

                var unitId = reader.GetInt32(1);
                if (!units.Contains(unitId)) units.Add(unitId);

                var serial = reader.GetString(2);
                if (!string.IsNullOrWhiteSpace(serial)) lineSerials[lineId].Add(serial);
            }

            var cash = new Dictionary<int, decimal>();
            var vodafone = new Dictionary<int, decimal>();
            var insta = new Dictionary<int, decimal>();
            var refunds = new Dictionary<int, decimal>();

            reader.NextResult();
            while (reader.Read())
            {
                var invoiceId = reader.GetInt32(0);
                if (!byId.ContainsKey(invoiceId)) continue;

                var kind = reader.GetByte(1);
                var paymentKind = reader.GetByte(2);
                var amount = reader.GetDecimal(3);

                if (paymentKind == 2)
                {
                    refunds[invoiceId] = refunds.GetValueOrDefault(invoiceId) + amount;
                    continue;
                }

                var bucket = kind switch
                {
                    0 => cash,
                    1 => vodafone,
                    _ => insta
                };
                bucket[invoiceId] = bucket.GetValueOrDefault(invoiceId) + amount;
            }

            reader.NextResult();
            while (reader.Read())
            {
                var invoiceId = reader.GetInt32(0);
                if (!byId.TryGetValue(invoiceId, out var invoice)) continue;

                invoice.ActivityLog.Add(new PurchaseInvoiceActivityEntry
                {
                    Timestamp = AsLocal(reader.GetDateTime(1)),
                    ActionTitle = reader.GetString(2),
                    Details = reader.GetString(3),
                    ActorName = reader.GetString(4),
                    Amount = reader.GetDecimal(5)
                });
            }

            foreach (var (lineId, units) in lineUnits)
            {
                if (!lineById.TryGetValue(lineId, out var line)) continue;
                line.UnitRegistrationIds = units;
                line.SerialNumbers = lineSerials[lineId];
            }

            foreach (var invoice in headers)
            {
                invoice.CashPaid = cash.GetValueOrDefault(invoice.Id);
                invoice.VodafoneCashPaid = vodafone.GetValueOrDefault(invoice.Id);
                invoice.InstaPayPaid = insta.GetValueOrDefault(invoice.Id);
                invoice.SupplierRefundReceived = refunds.GetValueOrDefault(invoice.Id);
            }
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            throw Describe(ex, "تعذر تحميل فواتير المشتريات من قاعدة البيانات.");
        }

        target.Clear();
        foreach (var invoice in headers)
            target.Add(invoice);
    }

    /// <summary>Posts a purchase invoice: header, lines, stock units, payments and the activity trail.</summary>
    public SavedPurchase CreateInvoice(PurchaseInvoiceRequest request)
    {
        if (request.SupplierId <= 0) throw new PurchaseDataException("اختر موردًا مسجلًا في قاعدة البيانات قبل حفظ الفاتورة.");
        if (request.Lines.Count == 0) throw new PurchaseDataException("لا يمكن حفظ فاتورة شراء بدون أصناف.");

        foreach (var line in request.Lines)
        {
            if (line.Quantity <= 0) throw new PurchaseDataException($"كمية «{line.ProductName}» يجب أن تكون أكبر من صفر.");
            if (line.UnitPurchaseCost < 0m) throw new PurchaseDataException($"سعر شراء «{line.ProductName}» غير صحيح.");
        }

        var subtotal = request.Lines.Sum(l => l.Quantity * l.UnitPurchaseCost);
        var discount = Math.Min(subtotal, Math.Max(0m, request.DiscountAmount));
        var netTotal = Math.Max(0m, subtotal - discount);
        var requestedPaid = Math.Max(0m, request.CashPaid) + Math.Max(0m, request.VodafonePaid) + Math.Max(0m, request.InstaPayPaid);

        if (netTotal <= 0m) throw new PurchaseDataException("إجمالي الفاتورة صفر؛ أدخل كميات وأسعار شراء صحيحة.");
        if (requestedPaid > netTotal + 0.005m)
            throw new PurchaseDataException($"إجمالي المدفوع ({requestedPaid:0.00}) أكبر من صافي الفاتورة ({netTotal:0.00}).");

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var (supplierName, supplierPhone, supplierCompany, supplierAddress) =
                    LoadSupplierSnapshot(connection, transaction, request.SupplierId);

                var accounts = EnsureAccounts(connection, transaction);
                var invoiceNumber = NextDocumentNumber(connection, transaction, InvoiceSequence, DateTime.Now.Year, "PUR");
                var status = requestedPaid <= 0m
                    ? PurchaseInvoiceStatus.FullCredit
                    : requestedPaid < netTotal - 0.005m
                        ? PurchaseInvoiceStatus.PartialCredit
                        : PurchaseInvoiceStatus.Paid;

                int invoiceId;
                using (var insert = CreateCommand(connection, transaction, @"
INSERT dbo.PurchaseInvoices(InvoiceNumber,SupplierInvoiceNumber,SupplierId,SupplierNameSnapshot,SupplierPhoneSnapshot,
 SupplierCompanySnapshot,SupplierAddressSnapshot,IssuedAtUtc,InvoiceStatus,DiscountAmount,Notes,BuyerUserId)
OUTPUT inserted.Id
VALUES(@Number,@SupplierNumber,@Supplier,@Name,@Phone,@Company,@Address,@Issued,@Status,@Discount,@Notes,@Actor);"))
                {
                    insert.Parameters.Add("@Number", SqlDbType.NVarChar, 50).Value = invoiceNumber;
                    insert.Parameters.Add("@SupplierNumber", SqlDbType.NVarChar, 100).Value = Trim(request.SupplierInvoiceNumber, 100, "");
                    insert.Parameters.Add("@Supplier", SqlDbType.Int).Value = request.SupplierId;
                    insert.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value = supplierName;
                    insert.Parameters.Add("@Phone", SqlDbType.NVarChar, 30).Value = supplierPhone;
                    insert.Parameters.Add("@Company", SqlDbType.NVarChar, 200).Value = supplierCompany;
                    insert.Parameters.Add("@Address", SqlDbType.NVarChar, 500).Value = supplierAddress;
                    insert.Parameters.Add("@Issued", SqlDbType.DateTime2).Value = request.IssuedAt;
                    insert.Parameters.Add("@Status", SqlDbType.TinyInt).Value = (byte)status;
                    insert.Parameters.Add("@Discount", SqlDbType.Decimal).Value = discount;
                    insert.Parameters["@Discount"].Precision = 19;
                    insert.Parameters["@Discount"].Scale = 2;
                    insert.Parameters.Add("@Notes", SqlDbType.NVarChar, 1000).Value = Trim(request.Notes, 1000, "");
                    insert.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
                    invoiceId = (int)insert.ExecuteScalar()!;
                }

                var lineNumber = 1;
                var unitCount = 0;
                foreach (var line in request.Lines)
                {
                    int lineId = 0;
                    using (var insert = CreateCommand(connection, transaction, @"
INSERT dbo.PurchaseInvoiceLines(PurchaseInvoiceId,LineNumber,ProductId,BarcodeSnapshot,ProductNameSnapshot,
 CategoryNameSnapshot,UnitNameSnapshot,TrackSerialNumber,Quantity,ReturnedQuantity,UnitPurchaseCost)
OUTPUT inserted.Id
VALUES(@Invoice,@LineNumber,@Product,@Barcode,@Name,@Category,@Unit,@Track,@Quantity,0,@Cost);"))
                    {
                        insert.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
                        insert.Parameters.Add("@LineNumber", SqlDbType.Int).Value = lineNumber;
                        insert.Parameters.Add("@Product", SqlDbType.Int).Value = line.ProductId;
                        insert.Parameters.Add("@Barcode", SqlDbType.NVarChar, 100).Value = Trim(line.Barcode, 100, "");
                        insert.Parameters.Add("@Name", SqlDbType.NVarChar, 250).Value = Trim(line.ProductName, 250, "");
                        insert.Parameters.Add("@Category", SqlDbType.NVarChar, 200).Value = Trim(line.Category, 200, "");
                        insert.Parameters.Add("@Unit", SqlDbType.NVarChar, 50).Value = Trim(line.Unit, 50, "قطعة");
                        insert.Parameters.Add("@Track", SqlDbType.Bit).Value = line.TrackSerialNumber;
                        insert.Parameters.Add("@Quantity", SqlDbType.Int).Value = line.Quantity;
                        insert.Parameters.Add("@Cost", SqlDbType.Decimal).Value = line.UnitPurchaseCost;
                        insert.Parameters["@Cost"].Precision = 19;
                        insert.Parameters["@Cost"].Scale = 2;
                        lineId = (int)insert.ExecuteScalar()!;
                    }

                    unitCount += ReceiveUnits(connection, transaction, invoiceId, invoiceNumber, lineId, line);

                    using (var cost = CreateCommand(connection, transaction,
                        "UPDATE dbo.Products SET CostPrice=@Cost, UpdatedAtUtc=SYSUTCDATETIME() WHERE Id=@Id;"))
                    {
                        cost.Parameters.Add("@Cost", SqlDbType.Decimal).Value = line.UnitPurchaseCost;
                        cost.Parameters["@Cost"].Precision = 19;
                        cost.Parameters["@Cost"].Scale = 2;
                        cost.Parameters.Add("@Id", SqlDbType.Int).Value = line.ProductId;
                        cost.ExecuteNonQuery();
                    }

                    lineNumber++;
                }

                // Payments are applied in channel order and can never exceed the invoice net total.
                var remaining = netTotal;
                var cashApplied = ApplyPayment(connection, transaction, invoiceId, accounts,
                    FinancialAccountKind.Cash, request.CashPaid, ref remaining, supplierName, "سداد فاتورة شراء");
                var vodafoneApplied = ApplyPayment(connection, transaction, invoiceId, accounts,
                    FinancialAccountKind.VodafoneCash, request.VodafonePaid, ref remaining, supplierName, "سداد فاتورة شراء");
                var instaApplied = ApplyPayment(connection, transaction, invoiceId, accounts,
                    FinancialAccountKind.InstaPay, request.InstaPayPaid, ref remaining, supplierName, "سداد فاتورة شراء");

                var paidTotal = cashApplied + vodafoneApplied + instaApplied;
                var summary = BuildPaymentSummary(cashApplied, vodafoneApplied, instaApplied);
                AddActivity(connection, transaction, invoiceId, "اعتماد فاتورة شراء",
                    paidTotal > 0m
                        ? $"توريد {unitCount} قطعة من المورد «{supplierName}» — الصافي {netTotal:0.00} ج.م · {summary} · متبقٍ {Math.Max(0m, netTotal - paidTotal):0.00} ج.م آجل."
                        : $"توريد {unitCount} قطعة من المورد «{supplierName}» على الحساب الآجل بالكامل ({netTotal:0.00} ج.م).",
                    paidTotal > 0m ? paidTotal : netTotal);

                Audit(connection, transaction, "CreatePurchaseInvoice", invoiceId,
                    $"فاتورة شراء {invoiceNumber} بإجمالي {netTotal:0.00} ومدفوع {paidTotal:0.00}", "PurchaseInvoice");

                transaction.Commit();
                return new SavedPurchase(invoiceId, invoiceNumber, netTotal, paidTotal, Math.Max(0m, netTotal - paidTotal), status);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not PurchaseDataException))
        {
            throw Describe(ex, "تعذر حفظ فاتورة الشراء في قاعدة البيانات.");
        }
    }

    /// <summary>Records a supplier payment against the outstanding balance of a saved invoice.</summary>
    public void PayInvoice(PurchasePaymentRequest request)
    {
        var requested = Math.Max(0m, request.CashPaid) + Math.Max(0m, request.VodafonePaid) + Math.Max(0m, request.InstaPayPaid);
        if (requested <= 0m) throw new PurchaseDataException("أدخل مبلغ سداد أكبر من صفر.");
        if (request.Notes.Trim().Length > 1000) throw new PurchaseDataException("الملاحظات يجب ألا تتجاوز 1000 حرف.");

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var (supplierName, remaining, invoiceActive) = LoadInvoiceState(connection, transaction, request.InvoiceId);
                if (!invoiceActive)
                    throw new PurchaseDataException("لا يمكن السداد على فاتورة ملغاة في قاعدة البيانات.");
                if (remaining <= 0m)
                    throw new PurchaseDataException("لا يوجد رصيد آجل متبقٍ على هذه الفاتورة في قاعدة البيانات.");
                if (requested > remaining + 0.005m)
                    throw new PurchaseDataException($"المبلغ المطلوب ({requested:0.00}) أكبر من المتبقي ({remaining:0.00}).");

                var accounts = EnsureAccounts(connection, transaction);
                var left = remaining;
                var cash = ApplyPayment(connection, transaction, request.InvoiceId, accounts,
                    FinancialAccountKind.Cash, request.CashPaid, ref left, supplierName, "سداد دفعة من الآجل");
                var vodafone = ApplyPayment(connection, transaction, request.InvoiceId, accounts,
                    FinancialAccountKind.VodafoneCash, request.VodafonePaid, ref left, supplierName, "سداد دفعة من الآجل");
                var insta = ApplyPayment(connection, transaction, request.InvoiceId, accounts,
                    FinancialAccountKind.InstaPay, request.InstaPayPaid, ref left, supplierName, "سداد دفعة من الآجل");

                var paid = cash + vodafone + insta;
                var summary = BuildPaymentSummary(cash, vodafone, insta);
                var note = string.IsNullOrWhiteSpace(request.Notes) ? "" : $" — {request.Notes.Trim()}";

                RecalculateStatus(connection, transaction, request.InvoiceId);

                AddActivity(connection, transaction, request.InvoiceId,
                    Math.Max(0m, remaining - paid) <= 0.005m ? "سداد كامل الرصيد للمورد" : "سداد دفعة من الآجل للمورد",
                    $"سداد {summary} للمورد «{supplierName}»{note} · المتبقي بعد السداد {Math.Max(0m, remaining - paid):0.00} ج.م.",
                    paid);

                Audit(connection, transaction, "PayPurchaseInvoice", request.InvoiceId,
                    $"سداد {paid:0.00} على فاتورة شراء", "PurchaseInvoice");

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not PurchaseDataException))
        {
            throw Describe(ex, "تعذر تسجيل السداد في قاعدة البيانات.");
        }
    }

    /// <summary>Returns units to the supplier, adjusts the credit/cash and writes the return document.</summary>
    public PurchaseReturnResult ReturnToSupplier(PurchaseReturnRequest request)
    {
        if (request.Lines.Count == 0) throw new PurchaseDataException("حدد كمية واحدة على الأقل لإرجاعها للمورد.");
        if (request.Lines.Any(l => l.Quantity <= 0)) throw new PurchaseDataException("كميات المرتجع يجب أن تكون أكبر من صفر.");
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new PurchaseDataException("يرجى تحديد سبب المرتجع للمورد.");

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var (supplierName, remainingBefore, invoiceActive) = LoadInvoiceState(connection, transaction, request.InvoiceId);
                if (!invoiceActive)
                    throw new PurchaseDataException("لا يمكن تسجيل مرتجع على فاتورة ملغاة.");

                var accounts = EnsureAccounts(connection, transaction);
                var returnNumber = NextDocumentNumber(connection, transaction, ReturnSequence, DateTime.Now.Year, "PRT");

                int returnId;
                using (var insert = CreateCommand(connection, transaction, @"
INSERT dbo.PurchaseReturns(ReturnNumber,PurchaseInvoiceId,ReturnedAtUtc,Reason,ActorUserId)
OUTPUT inserted.Id
VALUES(@Number,@Invoice,SYSUTCDATETIME(),@Reason,@Actor);"))
                {
                    insert.Parameters.Add("@Number", SqlDbType.NVarChar, 50).Value = returnNumber;
                    insert.Parameters.Add("@Invoice", SqlDbType.Int).Value = request.InvoiceId;
                    insert.Parameters.Add("@Reason", SqlDbType.NVarChar, 1000).Value = Trim(request.Reason, 1000, "");
                    insert.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
                    returnId = (int)insert.ExecuteScalar()!;
                }

                var totalUnits = 0;
                var totalValue = 0m;
                var summaries = new List<string>();

                // One document row per invoice line: the schema enforces UNIQUE(PurchaseReturnId, PurchaseInvoiceLineId).
                var merged = request.Lines
                    .GroupBy(l => l.LineId)
                    .Select(g => new PurchaseReturnLineInput(g.Key, g.Sum(x => x.Quantity)))
                    .ToList();

                foreach (var input in merged)
                {
                    int lineId, productId, quantity, returnedQuantity;
                    decimal unitCost;
                    string productName;

                    using (var line = CreateCommand(connection, transaction, @"
SELECT Id, ProductId, Quantity, ReturnedQuantity, UnitPurchaseCost, ProductNameSnapshot
FROM dbo.PurchaseInvoiceLines WITH (UPDLOCK, HOLDLOCK)
WHERE Id=@Id AND PurchaseInvoiceId=@Invoice;"))
                    {
                        line.Parameters.Add("@Id", SqlDbType.Int).Value = input.LineId;
                        line.Parameters.Add("@Invoice", SqlDbType.Int).Value = request.InvoiceId;
                        using var reader = line.ExecuteReader();
                        if (!reader.Read())
                            throw new PurchaseDataException("أحد بنود الفاتورة لم يعد موجودًا في قاعدة البيانات؛ أعد تحميل الشاشة.");
                        lineId = reader.GetInt32(0);
                        productId = reader.GetInt32(1);
                        quantity = reader.GetInt32(2);
                        returnedQuantity = reader.GetInt32(3);
                        unitCost = reader.GetDecimal(4);
                        productName = reader.GetString(5);
                    }

                    var maxReturnable = Math.Max(0, quantity - returnedQuantity);
                    if (input.Quantity > maxReturnable)
                        throw new PurchaseDataException(
                            $"لا يمكن إرجاع {input.Quantity} من «{productName}» — المتاح للإرجاع {maxReturnable}.");

                    var units = LoadReturnableUnits(connection, transaction, lineId, input.Quantity, request.PreferDamagedFirst);
                    var returned = Math.Min(input.Quantity, units.Count);

                    foreach (var unit in units)
                        RemoveUnit(connection, transaction, unit.UnitId, unit.ProductId, unit.Cost, "ReturnToSupplier",
                            $"إرجاع للمورد — {returnNumber}", returnId, "PurchaseReturn");

                    // Extra returns without unit rows still move the stock ledger and the invoice line.
                    var untracked = input.Quantity - returned;
                    if (untracked > 0)
                    {
                        using var movement = CreateCommand(connection, transaction, @"
INSERT dbo.StockMovements(ProductId,InventoryUnitId,MovementType,QuantityDelta,UnitCost,ActorUserId,SourceEntityType,SourceEntityId,Reason)
VALUES(@Product,NULL,N'ReturnToSupplier',-@Quantity,@Cost,@Actor,N'PurchaseReturn',@Return,'إرجاع للمورد بدون وحدات مسجلة');");
                        movement.Parameters.Add("@Product", SqlDbType.Int).Value = productId;
                        movement.Parameters.Add("@Quantity", SqlDbType.Int).Value = untracked;
                        movement.Parameters.Add("@Cost", SqlDbType.Decimal).Value = unitCost;
                        movement.Parameters["@Cost"].Precision = 19;
                        movement.Parameters["@Cost"].Scale = 2;
                        movement.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
                        movement.Parameters.Add("@Return", SqlDbType.Int).Value = returnId;
                        movement.ExecuteNonQuery();
                    }

                    using (var update = CreateCommand(connection, transaction,
                        "UPDATE dbo.PurchaseInvoiceLines SET ReturnedQuantity = ReturnedQuantity + @Quantity WHERE Id=@Id;"))
                    {
                        update.Parameters.Add("@Quantity", SqlDbType.Int).Value = input.Quantity;
                        update.Parameters.Add("@Id", SqlDbType.Int).Value = lineId;
                        update.ExecuteNonQuery();
                    }

                    using (var document = CreateCommand(connection, transaction, @"
INSERT dbo.PurchaseReturnLines(PurchaseReturnId,PurchaseInvoiceLineId,Quantity,UnitPurchaseCost)
VALUES(@Return,@Line,@Quantity,@Cost);"))
                    {
                        document.Parameters.Add("@Return", SqlDbType.Int).Value = returnId;
                        document.Parameters.Add("@Line", SqlDbType.Int).Value = lineId;
                        document.Parameters.Add("@Quantity", SqlDbType.Int).Value = input.Quantity;
                        document.Parameters.Add("@Cost", SqlDbType.Decimal).Value = unitCost;
                        document.Parameters["@Cost"].Precision = 19;
                        document.Parameters["@Cost"].Scale = 2;
                    }

                    totalUnits += input.Quantity;
                    totalValue += input.Quantity * unitCost;
                    summaries.Add($"{input.Quantity}× {productName}");
                }

                totalValue = Math.Round(totalValue, 2, MidpointRounding.AwayFromZero);
                var creditApplied = Math.Min(totalValue, remainingBefore);
                var excess = Math.Max(0m, totalValue - creditApplied);

                var refundReceived = 0m;
                if (excess > 0m && TryRefundChannel(request.RefundChannel, out var refundKind))
                {
                    refundReceived = excess;
                    ReceiveRefund(connection, transaction, request.InvoiceId, accounts[refundKind], refundReceived,
                        supplierName, $"استرداد مرتجع مشتريات {returnNumber}");
                }

                RecalculateStatus(connection, transaction, request.InvoiceId);
                var (status, _, _) = LoadInvoiceStatus(connection, transaction, request.InvoiceId);

                var settlement = refundReceived > 0m
                    ? $"خصم {creditApplied:0.00} من الآجل + استرداد {refundReceived:0.00} ({request.RefundChannel})"
                    : excess > 0m
                        ? $"خصم {creditApplied:0.00} من الآجل ورصيد {excess:0.00} يُسوّى مع المورد لاحقًا"
                        : $"خصم {creditApplied:0.00} من الآجل";

                var noteSuffix = string.IsNullOrWhiteSpace(request.Notes) ? "" : $" — {request.Notes.Trim()}";
                AddActivity(connection, transaction, request.InvoiceId,
                    status == PurchaseInvoiceStatus.FullyReturned ? "مرتجع كلي للمورد" : "مرتجع جزئي للمورد",
                    $"مرتجع {returnNumber}: إرجاع {totalUnits} قطعة ({string.Join("، ", summaries)}) · السبب: {request.Reason.Trim()} · {settlement}{noteSuffix}.",
                    totalValue);

                Audit(connection, transaction, "PurchaseReturn", request.InvoiceId,
                    $"مرتجع {returnNumber} بقيمة {totalValue:0.00} على فاتورة شراء", "PurchaseInvoice");

                transaction.Commit();
                return new PurchaseReturnResult(returnNumber, totalUnits, totalValue, creditApplied, refundReceived, status);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not PurchaseDataException))
        {
            throw Describe(ex, "تعذر حفظ المرتجع في قاعدة البيانات.");
        }
    }

    /// <summary>Cancels the invoice: unsold stock leaves the warehouse and the paid money returns to its accounts.</summary>
    public void CancelInvoice(int invoiceId)
    {
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var (supplierName, _, invoiceActive) = LoadInvoiceState(connection, transaction, invoiceId);
                if (!invoiceActive) throw new PurchaseDataException("الفاتورة ملغاة بالفعل في قاعدة البيانات.");

                if (CountSoldUnits(connection, transaction, invoiceId) > 0)
                    throw new PurchaseDataException("لا يمكن إلغاء الفاتورة لوجود قطع مباعة للعملاء منها؛ استخدم «مرتجع للمورد» للقطع غير المباعة.");

                var accounts = EnsureAccounts(connection, transaction);
                var collected = LoadCollectedAmounts(connection, transaction, invoiceId);
                var netPaid = Math.Max(0m, collected.TotalPaid - collected.TotalRefunded);

                RemoveUnsoldUnits(connection, transaction, invoiceId);

                using (var status = CreateCommand(connection, transaction,
                    "UPDATE dbo.PurchaseInvoices SET InvoiceStatus=@Status, UpdatedAtUtc=SYSUTCDATETIME() WHERE Id=@Id;"))
                {
                    status.Parameters.Add("@Status", SqlDbType.TinyInt).Value = (byte)PurchaseInvoiceStatus.Cancelled;
                    status.Parameters.Add("@Id", SqlDbType.Int).Value = invoiceId;
                    status.ExecuteNonQuery();
                }

                if (netPaid > 0m)
                {
                    var left = netPaid;
                    var order = new[]
                    {
                        (FinancialAccountKind.Cash, collected.Cash),
                        (FinancialAccountKind.VodafoneCash, collected.Vodafone),
                        (FinancialAccountKind.InstaPay, collected.InstaPay)
                    };

                    foreach (var (kind, paidAmount) in order)
                    {
                        if (left <= 0m) break;
                        var portion = Math.Min(left, Math.Max(0m, paidAmount));
                        if (portion <= 0m) continue;
                        left -= portion;
                        ReceiveRefund(connection, transaction, invoiceId, accounts[kind], portion, supplierName,
                            "استرداد مبلغ فاتورة شراء ملغاة");
                    }

                    if (left > 0m)
                        ReceiveRefund(connection, transaction, invoiceId, accounts[FinancialAccountKind.Cash], left,
                            supplierName, "استرداد مبلغ فاتورة شراء ملغاة");
                }

                AddActivity(connection, transaction, invoiceId, "إلغاء فاتورة الشراء",
                    $"تم إلغاء الفاتورة وسحب القطع غير المباعة من المخزون واسترداد {netPaid:0.00} ج.م من المدفوعات.", netPaid);

                Audit(connection, transaction, "CancelPurchaseInvoice", invoiceId,
                    $"إلغاء فاتورة شراء واسترداد {netPaid:0.00}", "PurchaseInvoice");

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not PurchaseDataException))
        {
            throw Describe(ex, "تعذر إلغاء فاتورة الشراء في قاعدة البيانات.");
        }
    }

    /// <summary>Deletes the invoice permanently. Returns and payments keep the invoice alive in the ledger.</summary>
    public void DeleteInvoice(int invoiceId)
    {
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var (_, _, _) = LoadInvoiceState(connection, transaction, invoiceId);

                if (CountSoldUnits(connection, transaction, invoiceId) > 0)
                    throw new PurchaseDataException("لا يمكن حذف الفاتورة نهائيًا لوجود قطع مباعة للعملاء منها. استخدم «مرتجع للمورد» للقطع المتبقية.");

                using (var guard = CreateCommand(connection, transaction, @"
SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.PurchaseReturns WHERE PurchaseInvoiceId=@Id)
              OR EXISTS (SELECT 1 FROM dbo.PurchasePayments WHERE PurchaseInvoiceId=@Id)
            THEN 1 ELSE 0 END;"))
                {
                    guard.Parameters.Add("@Id", SqlDbType.Int).Value = invoiceId;
                    if (Convert.ToInt32(guard.ExecuteScalar()) == 1)
                        throw new PurchaseDataException(
                            "لا يمكن الحذف النهائي: على الفاتورة سداد أو مرتجع مسجّل في السجل. استخدم «إلغاء الفاتورة» بدلًا من الحذف.");
                }

                RemoveUnsoldUnits(connection, transaction, invoiceId);

                using (var lines = CreateCommand(connection, transaction, @"
DELETE dbo.PurchaseInvoiceActivities WHERE PurchaseInvoiceId=@Id;
DELETE dbo.PurchaseInvoiceLines WHERE PurchaseInvoiceId=@Id;
DELETE dbo.PurchaseInvoices WHERE Id=@Id;"))
                {
                    lines.Parameters.Add("@Id", SqlDbType.Int).Value = invoiceId;
                    lines.ExecuteNonQuery();
                }

                Audit(connection, transaction, "DeletePurchaseInvoice", invoiceId,
                    "حذف نهائي لفاتورة شراء بدون سداد أو مرتجع", "PurchaseInvoice");

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not PurchaseDataException))
        {
            throw Describe(ex, "تعذر حذف فاتورة الشراء من قاعدة البيانات.");
        }
    }

    /// <summary>Saves the edit dialog: header, lines, units and the payment deltas.</summary>
    public void UpdateInvoice(PurchaseInvoiceEdit request)
    {
        if (request.Lines.Count == 0) throw new PurchaseDataException("يجب أن تحتوي الفاتورة على صنف واحد على الأقل.");

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var (_, _, invoiceActive) = LoadInvoiceState(connection, transaction, request.InvoiceId);
                if (!invoiceActive) throw new PurchaseDataException("لا يمكن تعديل فاتورة ملغاة في قاعدة البيانات.");

                var (supplierName, supplierPhone, supplierCompany, supplierAddress) =
                    LoadSupplierSnapshot(connection, transaction, request.SupplierId);

                var existingLines = new Dictionary<int, (int ProductId, int Quantity, int ReturnedQuantity, decimal Cost, string Name)>();
                using (var read = CreateCommand(connection, transaction, @"
SELECT Id, ProductId, Quantity, ReturnedQuantity, UnitPurchaseCost, ProductNameSnapshot
FROM dbo.PurchaseInvoiceLines WITH (UPDLOCK, HOLDLOCK) WHERE PurchaseInvoiceId=@Invoice;"))
                {
                    read.Parameters.Add("@Invoice", SqlDbType.Int).Value = request.InvoiceId;
                    using var reader = read.ExecuteReader();
                    while (reader.Read())
                        existingLines[reader.GetInt32(0)] = (reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
                            reader.GetDecimal(4), reader.GetString(5));
                }

                var keptIds = request.Lines.Where(l => l.LineId.HasValue).Select(l => l.LineId!.Value).ToHashSet();
                var removedIds = existingLines.Keys.Where(id => !keptIds.Contains(id)).ToList();

                foreach (var removedId in removedIds)
                {
                    if (existingLines[removedId].ReturnedQuantity > 0)
                        throw new PurchaseDataException(
                            $"لا يمكن حذف بند «{existingLines[removedId].Name}» لوجود قطع مرتجعة منه للمورد.");
                    if (CountLineUnits(connection, transaction, removedId, soldOnly: true) > 0)
                        throw new PurchaseDataException(
                            $"لا يمكن حذف بند «{existingLines[removedId].Name}» لوجود قطع مباعة للعملاء منه.");
                    DeleteLine(connection, transaction, removedId, request.InvoiceId);
                }

                // Suppliers can change: re-stamp the header snapshot with the newly selected supplier.
                using (var header = CreateCommand(connection, transaction, @"
UPDATE dbo.PurchaseInvoices
SET SupplierId=@Supplier, SupplierNameSnapshot=@Name, SupplierPhoneSnapshot=@Phone,
    SupplierCompanySnapshot=@Company, SupplierAddressSnapshot=@Address, SupplierInvoiceNumber=@SupplierNumber,
    DiscountAmount=@Discount, Notes=@Notes, UpdatedAtUtc=SYSUTCDATETIME()
WHERE Id=@Id;"))
                {
                    header.Parameters.Add("@Supplier", SqlDbType.Int).Value = request.SupplierId;
                    header.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value = supplierName;
                    header.Parameters.Add("@Phone", SqlDbType.NVarChar, 30).Value = supplierPhone;
                    header.Parameters.Add("@Company", SqlDbType.NVarChar, 200).Value = supplierCompany;
                    header.Parameters.Add("@Address", SqlDbType.NVarChar, 500).Value = supplierAddress;
                    header.Parameters.Add("@SupplierNumber", SqlDbType.NVarChar, 100).Value = Trim(request.SupplierInvoiceNumber, 100, "");
                    header.Parameters.Add("@Discount", SqlDbType.Decimal).Value = Math.Max(0m, request.DiscountAmount);
                    header.Parameters["@Discount"].Precision = 19;
                    header.Parameters["@Discount"].Scale = 2;
                    header.Parameters.Add("@Notes", SqlDbType.NVarChar, 1000).Value = Trim(request.Notes, 1000, "");
                    header.Parameters.Add("@Id", SqlDbType.Int).Value = request.InvoiceId;
                    header.ExecuteNonQuery();
                }

                var invoiceNumber = InvoiceNumberOf(connection, transaction, request.InvoiceId);
                var lineNumber = 1;
                foreach (var line in request.Lines)
                {
                    var normalized = new PurchaseLineRequest(line.ProductId, line.Barcode, line.ProductName,
                        line.Category, line.Unit, line.TrackSerialNumber, line.Quantity, line.UnitPurchaseCost);

                    if (line.LineId is { } existingId && existingLines.TryGetValue(existingId, out var stored))
                    {
                        var sold = CountLineUnits(connection, transaction, existingId, soldOnly: true);
                        if (line.Quantity < stored.ReturnedQuantity + sold)
                            throw new PurchaseDataException(
                                $"كمية «{stored.Name}» لا يمكن أن تقل عن {stored.ReturnedQuantity + sold} (مرتجع/مباع).");

                        using (var update = CreateCommand(connection, transaction, @"
UPDATE dbo.PurchaseInvoiceLines
SET LineNumber=@LineNumber, ProductId=@Product, BarcodeSnapshot=@Barcode, ProductNameSnapshot=@Name,
    CategoryNameSnapshot=@Category, UnitNameSnapshot=@Unit, TrackSerialNumber=@Track, Quantity=@Quantity, UnitPurchaseCost=@Cost
WHERE Id=@Id;"))
                        {
                            update.Parameters.Add("@LineNumber", SqlDbType.Int).Value = lineNumber;
                            update.Parameters.Add("@Product", SqlDbType.Int).Value = line.ProductId;
                            update.Parameters.Add("@Barcode", SqlDbType.NVarChar, 100).Value = Trim(line.Barcode, 100, "");
                            update.Parameters.Add("@Name", SqlDbType.NVarChar, 250).Value = Trim(line.ProductName, 250, "");
                            update.Parameters.Add("@Category", SqlDbType.NVarChar, 200).Value = Trim(line.Category, 200, "");
                            update.Parameters.Add("@Unit", SqlDbType.NVarChar, 50).Value = Trim(line.Unit, 50, "قطعة");
                            update.Parameters.Add("@Track", SqlDbType.Bit).Value = line.TrackSerialNumber;
                            update.Parameters.Add("@Quantity", SqlDbType.Int).Value = line.Quantity;
                            update.Parameters.Add("@Cost", SqlDbType.Decimal).Value = line.UnitPurchaseCost;
                            update.Parameters["@Cost"].Precision = 19;
                            update.Parameters["@Cost"].Scale = 2;
                            update.Parameters.Add("@Id", SqlDbType.Int).Value = existingId;
                            update.ExecuteNonQuery();
                        }

                        // The line must keep (quantity - returned - sold) unsold unit rows; sold units are untouchable.
                        var soldUnits = CountLineUnits(connection, transaction, existingId, soldOnly: true);
                        var activeUnits = CountLineUnits(connection, transaction, existingId, soldOnly: false) - soldUnits;
                        var targetActive = line.Quantity - stored.ReturnedQuantity - soldUnits;
                        if (targetActive > activeUnits)
                        {
                            var extra = new PurchaseLineRequest(line.ProductId, line.Barcode, line.ProductName, line.Category,
                                line.Unit, line.TrackSerialNumber, targetActive - activeUnits, line.UnitPurchaseCost);
                            ReceiveUnits(connection, transaction, request.InvoiceId, invoiceNumber, existingId, extra);
                        }
                        else if (targetActive < activeUnits)
                        {
                            TrimLineUnits(connection, transaction, existingId, request.InvoiceId, activeUnits - targetActive);
                        }

                        using (var cost = CreateCommand(connection, transaction,
                            "UPDATE dbo.Products SET CostPrice=@Cost, UpdatedAtUtc=SYSUTCDATETIME() WHERE Id=@Id;"))
                        {
                            cost.Parameters.Add("@Cost", SqlDbType.Decimal).Value = line.UnitPurchaseCost;
                            cost.Parameters["@Cost"].Precision = 19;
                            cost.Parameters["@Cost"].Scale = 2;
                            cost.Parameters.Add("@Id", SqlDbType.Int).Value = line.ProductId;
                            cost.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        int newLineId = 0;
                        using (var insert = CreateCommand(connection, transaction, @"
INSERT dbo.PurchaseInvoiceLines(PurchaseInvoiceId,LineNumber,ProductId,BarcodeSnapshot,ProductNameSnapshot,
 CategoryNameSnapshot,UnitNameSnapshot,TrackSerialNumber,Quantity,ReturnedQuantity,UnitPurchaseCost)
OUTPUT inserted.Id
VALUES(@Invoice,@LineNumber,@Product,@Barcode,@Name,@Category,@Unit,@Track,@Quantity,0,@Cost);"))
                        {
                            insert.Parameters.Add("@Invoice", SqlDbType.Int).Value = request.InvoiceId;
                            insert.Parameters.Add("@LineNumber", SqlDbType.Int).Value = lineNumber;
                            insert.Parameters.Add("@Product", SqlDbType.Int).Value = line.ProductId;
                            insert.Parameters.Add("@Barcode", SqlDbType.NVarChar, 100).Value = Trim(line.Barcode, 100, "");
                            insert.Parameters.Add("@Name", SqlDbType.NVarChar, 250).Value = Trim(line.ProductName, 250, "");
                            insert.Parameters.Add("@Category", SqlDbType.NVarChar, 200).Value = Trim(line.Category, 200, "");
                            insert.Parameters.Add("@Unit", SqlDbType.NVarChar, 50).Value = Trim(line.Unit, 50, "قطعة");
                            insert.Parameters.Add("@Track", SqlDbType.Bit).Value = line.TrackSerialNumber;
                            insert.Parameters.Add("@Quantity", SqlDbType.Int).Value = line.Quantity;
                            insert.Parameters.Add("@Cost", SqlDbType.Decimal).Value = line.UnitPurchaseCost;
                            insert.Parameters["@Cost"].Precision = 19;
                            insert.Parameters["@Cost"].Scale = 2;
                            newLineId = (int)insert.ExecuteScalar()!;
                        }

                        ReceiveUnits(connection, transaction, request.InvoiceId, invoiceNumber, newLineId, normalized);
                    }

                    lineNumber++;
                }

                // Payments: apply only the difference against what SQL already has for each channel.
                var accounts = EnsureAccounts(connection, transaction);
                var collected = LoadCollectedAmounts(connection, transaction, request.InvoiceId);
                SyncChannel(connection, transaction, request.InvoiceId, accounts, FinancialAccountKind.Cash,
                    collected.Cash, request.CashPaid, supplierName);
                SyncChannel(connection, transaction, request.InvoiceId, accounts, FinancialAccountKind.VodafoneCash,
                    collected.Vodafone, request.VodafonePaid, supplierName);
                SyncChannel(connection, transaction, request.InvoiceId, accounts, FinancialAccountKind.InstaPay,
                    collected.InstaPay, request.InstaPayPaid, supplierName);

                RecalculateStatus(connection, transaction, request.InvoiceId);

                AddActivity(connection, transaction, request.InvoiceId, "تعديل شامل لفاتورة الشراء",
                    $"المورد: {supplierName} · {lineNumber - 1} بند · صافي بعد التعديل {request.Lines.Sum(l => l.Quantity * l.UnitPurchaseCost) - Math.Max(0m, request.DiscountAmount):0.00} ج.م.",
                    request.CashPaid + request.VodafonePaid + request.InstaPayPaid);

                Audit(connection, transaction, "UpdatePurchaseInvoice", request.InvoiceId,
                    "تعديل فاتورة شراء ومزامنة المخزون والمدفوعات", "PurchaseInvoice");

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not PurchaseDataException))
        {
            throw Describe(ex, "تعذر حفظ تعديلات فاتورة الشراء في قاعدة البيانات.");
        }
    }

    // ------------------------------------------------------------------ helpers

    private static (string Name, string Phone, string Company, string Address) LoadSupplierSnapshot(
        SqlConnection connection, SqlTransaction transaction, int supplierId)
    {
        using var command = CreateCommand(connection, transaction, @"
SELECT Name, ISNULL(Phone,N''), ISNULL(CompanyName,N''), ISNULL(Address,N'')
FROM dbo.Suppliers WITH (UPDLOCK, HOLDLOCK) WHERE Id=@Id AND IsActive=1;");
        command.Parameters.Add("@Id", SqlDbType.Int).Value = supplierId;
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new PurchaseDataException("المورد المختار غير موجود في قاعدة البيانات؛ حدّث دليل الموردين ثم اختره من جديد.");

        return (Trim(reader.GetString(0), 200, "مورد"),
            Trim(reader.GetString(1), 30, ""),
            Trim(reader.GetString(2), 200, ""),
            Trim(reader.GetString(3), 500, ""));
    }

    private static (string SupplierName, decimal Remaining, bool IsActive) LoadInvoiceState(
        SqlConnection connection, SqlTransaction transaction, int invoiceId)
    {
        var (supplierName, gross, discount, netPaid, isActive) = LoadInvoiceTotals(connection, transaction, invoiceId);
        var net = Math.Max(0m, gross - discount);
        return (supplierName, Math.Max(0m, net - netPaid), isActive);
    }

    private static (PurchaseInvoiceStatus Status, decimal Remaining, bool IsActive) LoadInvoiceStatus(
        SqlConnection connection, SqlTransaction transaction, int invoiceId)
    {
        using var command = CreateCommand(connection, transaction,
            "SELECT InvoiceStatus FROM dbo.PurchaseInvoices WHERE Id=@Id;");
        command.Parameters.Add("@Id", SqlDbType.Int).Value = invoiceId;
        var value = command.ExecuteScalar();
        if (value is null) throw new PurchaseDataException("الفاتورة غير موجودة في قاعدة البيانات؛ أعد تحميل الشاشة.");
        var status = (PurchaseInvoiceStatus)Convert.ToByte(value);
        var remaining = LoadInvoiceState(connection, transaction, invoiceId).Remaining;
        return (status, remaining, status != PurchaseInvoiceStatus.Cancelled);
    }

    private static (string SupplierName, decimal Gross, decimal Discount, decimal NetPaid, bool IsActive) LoadInvoiceTotals(
        SqlConnection connection, SqlTransaction transaction, int invoiceId)
    {
        using var command = CreateCommand(connection, transaction, @"
SELECT i.SupplierNameSnapshot, i.InvoiceStatus, i.DiscountAmount,
       ISNULL((SELECT SUM((l.Quantity - l.ReturnedQuantity) * l.UnitPurchaseCost)
               FROM dbo.PurchaseInvoiceLines l WHERE l.PurchaseInvoiceId=i.Id), 0),
       ISNULL((SELECT SUM(CASE WHEN p.PaymentKind=1 THEN p.Amount ELSE -p.Amount END)
               FROM dbo.PurchasePayments p WHERE p.PurchaseInvoiceId=i.Id), 0)
FROM dbo.PurchaseInvoices i WHERE i.Id=@Id;");
        command.Parameters.Add("@Id", SqlDbType.Int).Value = invoiceId;
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new PurchaseDataException("الفاتورة غير موجودة في قاعدة البيانات؛ أعد تحميل الشاشة.");

        var status = (PurchaseInvoiceStatus)reader.GetByte(1);
        var gross = Math.Max(0m, reader.GetDecimal(3));
        var discount = reader.GetDecimal(2);
        var netPaid = reader.GetDecimal(4);
        return (reader.GetString(0), gross, discount, netPaid, status != PurchaseInvoiceStatus.Cancelled);
    }

    private static string InvoiceNumberOf(SqlConnection connection, SqlTransaction transaction, int invoiceId)
    {
        using var command = CreateCommand(connection, transaction,
            "SELECT InvoiceNumber FROM dbo.PurchaseInvoices WHERE Id=@Id;");
        command.Parameters.Add("@Id", SqlDbType.Int).Value = invoiceId;
        return command.ExecuteScalar() as string ?? "PUR-?";
    }

    /// <summary>Creates one stock unit per purchased piece and returns how many were registered.</summary>
    private int ReceiveUnits(SqlConnection connection, SqlTransaction transaction, int invoiceId,
        string invoiceNumber, int lineId, PurchaseLineRequest line)
    {
        for (var i = 0; i < line.Quantity; i++)
        {
            int unitId;
            using (var insert = CreateCommand(connection, transaction, @"
INSERT dbo.InventoryUnits(ProductId,SupplierId,PurchaseInvoiceNumber,PurchaseCost,UnitStatus)
OUTPUT inserted.Id
SELECT @Product, i.SupplierId, @InvoiceNumber, @Cost, 0
FROM dbo.PurchaseInvoices i WHERE i.Id=@Invoice;"))
            {
                insert.Parameters.Add("@Product", SqlDbType.Int).Value = line.ProductId;
                insert.Parameters.Add("@InvoiceNumber", SqlDbType.NVarChar, 100).Value = Trim(invoiceNumber, 100, "");
                // The SELECT reads the supplier from this invoice; without @Invoice SQL Server
                // raises 137 and the whole purchase transaction rolls back.
                insert.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
                insert.Parameters.Add("@Cost", SqlDbType.Decimal).Value = line.UnitPurchaseCost;
                insert.Parameters["@Cost"].Precision = 19;
                insert.Parameters["@Cost"].Scale = 2;
                unitId = (int)insert.ExecuteScalar()!;
            }

            if (line.TrackSerialNumber)
            {
                using var serial = CreateCommand(connection, transaction,
                    "UPDATE dbo.InventoryUnits SET SerialNumber=@Serial WHERE Id=@Id;");
                serial.Parameters.Add("@Serial", SqlDbType.NVarChar, 150).Value = $"SN-{invoiceNumber}-{unitId}";
                serial.Parameters.Add("@Id", SqlDbType.Int).Value = unitId;
                serial.ExecuteNonQuery();
            }

            using (var link = CreateCommand(connection, transaction,
                "INSERT dbo.PurchaseInvoiceLineUnits(PurchaseInvoiceLineId,InventoryUnitId) VALUES(@Line,@Unit);"))
            {
                link.Parameters.Add("@Line", SqlDbType.Int).Value = lineId;
                link.Parameters.Add("@Unit", SqlDbType.Int).Value = unitId;
                link.ExecuteNonQuery();
            }

            using var movement = CreateCommand(connection, transaction, @"
INSERT dbo.StockMovements(ProductId,InventoryUnitId,MovementType,QuantityDelta,UnitCost,ActorUserId,SourceEntityType,SourceEntityId,Reason)
VALUES(@Product,@Unit,N'Supply',1,@Cost,@Actor,N'PurchaseInvoice',@Invoice,@Reason);");
            movement.Parameters.Add("@Product", SqlDbType.Int).Value = line.ProductId;
            movement.Parameters.Add("@Unit", SqlDbType.Int).Value = unitId;
            movement.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
            movement.Parameters.Add("@Cost", SqlDbType.Decimal).Value = line.UnitPurchaseCost;
            movement.Parameters["@Cost"].Precision = 19;
            movement.Parameters["@Cost"].Scale = 2;
            movement.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
            movement.Parameters.Add("@Reason", SqlDbType.NVarChar, 1000).Value = $"توريد فاتورة شراء {invoiceNumber}";
            movement.ExecuteNonQuery();
        }

        return line.Quantity;
    }

    private static int CountLineUnits(SqlConnection connection, SqlTransaction transaction, int lineId, bool soldOnly)
    {
        using var command = CreateCommand(connection, transaction, @"
SELECT COUNT(*)
FROM dbo.PurchaseInvoiceLineUnits lu
JOIN dbo.InventoryUnits iu ON iu.Id = lu.InventoryUnitId
WHERE lu.PurchaseInvoiceLineId = @Line AND (@SoldOnly = 0 OR iu.UnitStatus = 1);");
        command.Parameters.Add("@Line", SqlDbType.Int).Value = lineId;
        command.Parameters.Add("@SoldOnly", SqlDbType.Bit).Value = soldOnly;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static int CountSoldUnits(SqlConnection connection, SqlTransaction transaction, int invoiceId)
    {
        using var command = CreateCommand(connection, transaction, @"
SELECT COUNT(*)
FROM dbo.PurchaseInvoiceLineUnits lu
JOIN dbo.InventoryUnits iu ON iu.Id = lu.InventoryUnitId
JOIN dbo.PurchaseInvoiceLines l ON l.Id = lu.PurchaseInvoiceLineId
WHERE l.PurchaseInvoiceId = @Invoice AND iu.UnitStatus = 1;");
        command.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>Drops the newest unsold units of a line when the operator shrinks the quantity.</summary>
    private void TrimLineUnits(SqlConnection connection, SqlTransaction transaction, int lineId, int invoiceId, int count)
    {
        if (count <= 0) return;

        var units = new List<(int UnitId, int ProductId, decimal Cost)>();
        using (var select = CreateCommand(connection, transaction, @"
SELECT TOP (@Count) iu.Id, iu.ProductId, iu.PurchaseCost
FROM dbo.PurchaseInvoiceLineUnits lu
JOIN dbo.InventoryUnits iu ON iu.Id = lu.InventoryUnitId
WHERE lu.PurchaseInvoiceLineId = @Line AND iu.UnitStatus IN (0,2)
ORDER BY CASE WHEN iu.UnitStatus = 0 THEN 0 ELSE 1 END, iu.Id DESC;"))
        {
            select.Parameters.Add("@Count", SqlDbType.Int).Value = count;
            select.Parameters.Add("@Line", SqlDbType.Int).Value = lineId;
            using var reader = select.ExecuteReader();
            while (reader.Read())
                units.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2)));
        }

        foreach (var unit in units)
            RemoveUnit(connection, transaction, unit.UnitId, unit.ProductId, unit.Cost, "PurchaseAdjustment",
                "تقليص كمية بند في فاتورة شراء", invoiceId, "PurchaseInvoice");
    }

    /// <summary>Removes a whole line: every unsold unit leaves the warehouse and the movement trail stays readable.</summary>
    private void DeleteLine(SqlConnection connection, SqlTransaction transaction, int lineId, int invoiceId)
    {
        foreach (var unit in LoadUnsoldUnits(connection, transaction, "lu.PurchaseInvoiceLineId = @Key", lineId))
            RemoveUnit(connection, transaction, unit.UnitId, unit.ProductId, unit.Cost, "PurchaseAdjustment",
                "حذف بند من فاتورة شراء", invoiceId, "PurchaseInvoice");

        using var delete = CreateCommand(connection, transaction, "DELETE dbo.PurchaseInvoiceLines WHERE Id=@Id;");
        delete.Parameters.Add("@Id", SqlDbType.Int).Value = lineId;
        delete.ExecuteNonQuery();
    }

    /// <summary>Every unsold unit (available or damaged) that still hangs off a selection key.</summary>
    private static List<(int UnitId, int ProductId, decimal Cost)> LoadUnsoldUnits(
        SqlConnection connection, SqlTransaction transaction, string keyPredicate, int key)
    {
        var units = new List<(int UnitId, int ProductId, decimal Cost)>();
        using var command = CreateCommand(connection, transaction, $@"
SELECT iu.Id, iu.ProductId, iu.PurchaseCost
FROM dbo.PurchaseInvoiceLineUnits lu
JOIN dbo.InventoryUnits iu ON iu.Id = lu.InventoryUnitId
JOIN dbo.PurchaseInvoiceLines l ON l.Id = lu.PurchaseInvoiceLineId
WHERE {keyPredicate} AND iu.UnitStatus IN (0,2);");
        command.Parameters.Add("@Key", SqlDbType.Int).Value = key;
        using var reader = command.ExecuteReader();
        while (reader.Read())
            units.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2)));
        return units;
    }

    /// <summary>
    /// Removes one purchased unit from the warehouse while keeping the movement ledger readable:
    /// the historical rows lose their dangling unit id and a signed movement records the exit.
    /// </summary>
    private void RemoveUnit(SqlConnection connection, SqlTransaction transaction, int unitId, int productId,
        decimal cost, string movementType, string reason, int sourceId, string sourceType)
    {
        using (var detach = CreateCommand(connection, transaction,
            "UPDATE dbo.StockMovements SET InventoryUnitId=NULL WHERE InventoryUnitId=@Unit;"))
        {
            detach.Parameters.Add("@Unit", SqlDbType.Int).Value = unitId;
            detach.ExecuteNonQuery();
        }

        using (var links = CreateCommand(connection, transaction, "DELETE dbo.PurchaseInvoiceLineUnits WHERE InventoryUnitId=@Unit;"))
        {
            links.Parameters.Add("@Unit", SqlDbType.Int).Value = unitId;
            links.ExecuteNonQuery();
        }

        using (var unit = CreateCommand(connection, transaction, "DELETE dbo.InventoryUnits WHERE Id=@Unit;"))
        {
            unit.Parameters.Add("@Unit", SqlDbType.Int).Value = unitId;
            unit.ExecuteNonQuery();
        }

        using var movement = CreateCommand(connection, transaction, @"
INSERT dbo.StockMovements(ProductId,InventoryUnitId,MovementType,QuantityDelta,UnitCost,ActorUserId,SourceEntityType,SourceEntityId,Reason)
VALUES(@Product,NULL,@Type,-1,@Cost,@Actor,@SourceType,@Source,@Reason);");
        movement.Parameters.Add("@Product", SqlDbType.Int).Value = productId;
        movement.Parameters.Add("@Type", SqlDbType.NVarChar, 40).Value = movementType;
        movement.Parameters.Add("@Cost", SqlDbType.Decimal).Value = cost;
        movement.Parameters["@Cost"].Precision = 19;
        movement.Parameters["@Cost"].Scale = 2;
        movement.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
        movement.Parameters.Add("@SourceType", SqlDbType.NVarChar, 50).Value = sourceType;
        movement.Parameters.Add("@Source", SqlDbType.Int).Value = sourceId;
        movement.Parameters.Add("@Reason", SqlDbType.NVarChar, 1000).Value = Trim(reason, 1000, "");
        movement.ExecuteNonQuery();
    }

    private static List<(int UnitId, int ProductId, decimal Cost)> LoadReturnableUnits(
        SqlConnection connection, SqlTransaction transaction, int lineId, int quantity, bool damagedFirst)
    {
        var units = new List<(int UnitId, int ProductId, decimal Cost)>();
        using var command = CreateCommand(connection, transaction, @"
SELECT TOP (@Quantity) iu.Id, iu.ProductId, iu.PurchaseCost
FROM dbo.PurchaseInvoiceLineUnits lu
JOIN dbo.InventoryUnits iu ON iu.Id = lu.InventoryUnitId
WHERE lu.PurchaseInvoiceLineId = @Line AND iu.UnitStatus IN (0,2)
ORDER BY CASE WHEN @DamagedFirst = 1
              THEN CASE WHEN iu.UnitStatus = 2 THEN 0 ELSE 1 END
              ELSE CASE WHEN iu.UnitStatus = 0 THEN 0 ELSE 1 END END,
         iu.Id DESC;");
        command.Parameters.Add("@Quantity", SqlDbType.Int).Value = quantity;
        command.Parameters.Add("@Line", SqlDbType.Int).Value = lineId;
        command.Parameters.Add("@DamagedFirst", SqlDbType.Bit).Value = damagedFirst;
        using var reader = command.ExecuteReader();
        while (reader.Read())
            units.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2)));
        return units;
    }

    /// <summary>Drops every unsold unit of an invoice (cancel/delete) and keeps one signed movement per unit.</summary>
    private void RemoveUnsoldUnits(SqlConnection connection, SqlTransaction transaction, int invoiceId)
    {
        foreach (var unit in LoadUnsoldUnits(connection, transaction, "l.PurchaseInvoiceId = @Key", invoiceId))
            RemoveUnit(connection, transaction, unit.UnitId, unit.ProductId, unit.Cost, "PurchaseCancel",
                "سحب مخزون فاتورة شراء ملغاة أو محذوفة", invoiceId, "PurchaseInvoice");
    }

    private decimal ApplyPayment(SqlConnection connection, SqlTransaction transaction, int invoiceId,
        IReadOnlyDictionary<FinancialAccountKind, int> accounts, FinancialAccountKind kind, decimal amount,
        ref decimal remainingToApply, string supplierName, string note)
    {
        if (amount <= 0m || remainingToApply <= 0m) return 0m;

        var applied = Math.Round(Math.Min(amount, remainingToApply), 2, MidpointRounding.AwayFromZero);
        if (applied <= 0m) return 0m;
        remainingToApply -= applied;

        var accountId = accounts[kind];
        InsertPayment(connection, transaction, invoiceId, accountId, paymentKind: 1, applied, note);
        var balanceAfter = AdjustBalance(connection, transaction, accountId, -applied);
        InsertLedger(connection, transaction, accountId, incoming: false, "سداد مشتريات", applied,
            balanceAfter, supplierName, $"{note} — فاتورة شراء", invoiceId, kind != FinancialAccountKind.Cash);
        return applied;
    }

    private void ReceiveRefund(SqlConnection connection, SqlTransaction transaction, int invoiceId, int accountId,
        decimal amount, string supplierName, string note)
    {
        if (amount <= 0m) return;

        InsertPayment(connection, transaction, invoiceId, accountId, paymentKind: 2, amount, note);
        var balanceAfter = AdjustBalance(connection, transaction, accountId, amount);
        InsertLedger(connection, transaction, accountId, incoming: true, "استرداد من مورد", amount,
            balanceAfter, supplierName, $"{note} — فاتورة شراء", invoiceId, false);
    }

    /// <summary>Brings one payment channel to the requested total by posting only the difference.</summary>
    private void SyncChannel(SqlConnection connection, SqlTransaction transaction, int invoiceId,
        IReadOnlyDictionary<FinancialAccountKind, int> accounts, FinancialAccountKind kind,
        decimal current, decimal target, string supplierName)
    {
        var delta = Math.Round(target - current, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(delta) < 0.005m) return;

        var accountId = accounts[kind];
        if (delta > 0m)
        {
            InsertPayment(connection, transaction, invoiceId, accountId, paymentKind: 1, delta, "تعديل سداد فاتورة شراء");
            var balanceAfter = AdjustBalance(connection, transaction, accountId, -delta);
            InsertLedger(connection, transaction, accountId, incoming: false, "سداد مشتريات", delta,
                balanceAfter, supplierName, "تعديل مدفوعات فاتورة شراء", invoiceId, kind != FinancialAccountKind.Cash);
        }
        else
        {
            var refund = Math.Abs(delta);
            InsertPayment(connection, transaction, invoiceId, accountId, paymentKind: 2, refund, "تسوية سداد فاتورة شراء");
            var balanceAfter = AdjustBalance(connection, transaction, accountId, refund);
            InsertLedger(connection, transaction, accountId, incoming: true, "تسوية مدفوعات مشتريات", refund,
                balanceAfter, supplierName, "تسوية مدفوعات فاتورة شراء", invoiceId, false);
        }
    }

    private void InsertPayment(SqlConnection connection, SqlTransaction transaction, int invoiceId,
        int accountId, int paymentKind, decimal amount, string note)
    {
        using var command = CreateCommand(connection, transaction, @"
INSERT dbo.PurchasePayments(PurchaseInvoiceId,FinancialAccountId,PaymentKind,Amount,PaidAtUtc,Note,ActorUserId)
VALUES(@Invoice,@Account,@Kind,@Amount,SYSUTCDATETIME(),@Note,@Actor);");
        command.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
        command.Parameters.Add("@Account", SqlDbType.Int).Value = accountId;
        command.Parameters.Add("@Kind", SqlDbType.TinyInt).Value = (byte)paymentKind;
        command.Parameters.Add("@Amount", SqlDbType.Decimal).Value = amount;
        command.Parameters["@Amount"].Precision = 19;
        command.Parameters["@Amount"].Scale = 2;
        command.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value = Trim(note, 1000, "");
        command.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
        command.ExecuteNonQuery();
    }

    private static decimal AdjustBalance(SqlConnection connection, SqlTransaction transaction, int accountId, decimal delta)
    {
        using var command = CreateCommand(connection, transaction, @"
UPDATE dbo.FinancialAccounts
SET CurrentBalance = CurrentBalance + @Delta, UpdatedAtUtc = SYSUTCDATETIME()
OUTPUT inserted.CurrentBalance
WHERE Id=@Id;");
        command.Parameters.Add("@Delta", SqlDbType.Decimal).Value = delta;
        command.Parameters["@Delta"].Precision = 19;
        command.Parameters["@Delta"].Scale = 2;
        command.Parameters.Add("@Id", SqlDbType.Int).Value = accountId;
        return (decimal)command.ExecuteScalar()!;
    }

    private void InsertLedger(SqlConnection connection, SqlTransaction transaction, int accountId, bool incoming,
        string category, decimal amount, decimal balanceAfter, string counterparty, string note, int invoiceId,
        bool countsTowardLimits)
    {
        using var command = CreateCommand(connection, transaction, @"
INSERT dbo.FinancialTransactions(FinancialAccountId,OccurredAtUtc,IsIncoming,Category,Amount,BalanceAfter,Counterparty,Note,
 ActorUserId,CountsTowardLimits,SourceEntityType,SourceEntityId)
VALUES(@Account,SYSUTCDATETIME(),@Incoming,@Category,@Amount,@Balance,@Counterparty,@Note,@Actor,@Limits,N'PurchaseInvoice',@Invoice);");
        command.Parameters.Add("@Account", SqlDbType.Int).Value = accountId;
        command.Parameters.Add("@Incoming", SqlDbType.Bit).Value = incoming;
        command.Parameters.Add("@Category", SqlDbType.NVarChar, 150).Value = Trim(category, 150, "مشتريات");
        command.Parameters.Add("@Amount", SqlDbType.Decimal).Value = amount;
        command.Parameters["@Amount"].Precision = 19;
        command.Parameters["@Amount"].Scale = 2;
        command.Parameters.Add("@Balance", SqlDbType.Decimal).Value = balanceAfter;
        command.Parameters["@Balance"].Precision = 19;
        command.Parameters["@Balance"].Scale = 2;
        command.Parameters.Add("@Counterparty", SqlDbType.NVarChar, 250).Value = Trim(counterparty, 250, "مورد");
        command.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value = Trim(note, 1000, "");
        command.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
        command.Parameters.Add("@Limits", SqlDbType.Bit).Value = countsTowardLimits;
        command.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
        command.ExecuteNonQuery();
    }

    private static void RecalculateStatus(SqlConnection connection, SqlTransaction transaction, int invoiceId)
    {
        using var command = CreateCommand(connection, transaction, @"
UPDATE dbo.PurchaseInvoices
SET InvoiceStatus = CASE
        WHEN InvoiceStatus = 3 THEN 3
        WHEN NOT EXISTS (SELECT 1 FROM dbo.PurchaseInvoiceLines WHERE PurchaseInvoiceId=@Id AND ReturnedQuantity < Quantity) THEN 5
        WHEN EXISTS (SELECT 1 FROM dbo.PurchaseInvoiceLines WHERE PurchaseInvoiceId=@Id AND ReturnedQuantity > 0) THEN 4
        WHEN ISNULL((SELECT SUM(CASE WHEN p.PaymentKind=1 THEN p.Amount ELSE -p.Amount END)
                     FROM dbo.PurchasePayments p WHERE p.PurchaseInvoiceId=@Id),0)
             >= ISNULL((SELECT SUM((l.Quantity - l.ReturnedQuantity) * l.UnitPurchaseCost)
                        FROM dbo.PurchaseInvoiceLines l WHERE l.PurchaseInvoiceId=@Id),0) - DiscountAmount THEN 0
        WHEN ISNULL((SELECT SUM(CASE WHEN p.PaymentKind=1 THEN p.Amount ELSE -p.Amount END)
                     FROM dbo.PurchasePayments p WHERE p.PurchaseInvoiceId=@Id),0) <= 0 THEN 2
        ELSE 1 END,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE Id = @Id;");
        command.Parameters.Add("@Id", SqlDbType.Int).Value = invoiceId;
        command.ExecuteNonQuery();
    }

    private static CollectedPurchaseAmounts LoadCollectedAmounts(
        SqlConnection connection, SqlTransaction transaction, int invoiceId)
    {
        // Explicitly typed: ref locals below need the concrete Dictionary overload of GetValueOrDefault.
        var paid = new Dictionary<byte, decimal>();
        var refunded = new Dictionary<byte, decimal>();

        using (var command = CreateCommand(connection, transaction, @"
SELECT a.AccountKind, p.PaymentKind, SUM(p.Amount)
FROM dbo.PurchasePayments p
JOIN dbo.FinancialAccounts a ON a.Id = p.FinancialAccountId
WHERE p.PurchaseInvoiceId = @Invoice
GROUP BY a.AccountKind, p.PaymentKind;"))
        {
            command.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var kind = reader.GetByte(0);
                var amount = reader.GetDecimal(2);
                if (reader.GetByte(1) == 2)
                {
                    refunded[kind] = refunded.TryGetValue(kind, out var currentRefund) ? currentRefund + amount : amount;
                }
                else
                {
                    paid[kind] = paid.TryGetValue(kind, out var currentPaid) ? currentPaid + amount : amount;
                }
            }
        }

        // Literal keys would be int here while the dictionary is keyed by byte; read each bucket explicitly.
        static decimal Amount(Dictionary<byte, decimal> source, byte kind) =>
            source.TryGetValue(kind, out var value) ? value : 0m;

        return new CollectedPurchaseAmounts(
            Amount(paid, 0), Amount(paid, 1), Amount(paid, 2),
            Amount(refunded, 0), Amount(refunded, 1), Amount(refunded, 2));
    }

    /// <summary>Maps the refund channel chosen in the return dialog to a financial account.</summary>
    private static bool TryRefundChannel(string channel, out FinancialAccountKind kind)
    {
        kind = FinancialAccountKind.Cash;
        if (string.IsNullOrWhiteSpace(channel)) return false;
        if (channel.Contains("لاحقًا", StringComparison.Ordinal)) return false;
        if (channel.Contains("فودافون", StringComparison.Ordinal)) { kind = FinancialAccountKind.VodafoneCash; return true; }
        if (channel.Contains("إنستاباي", StringComparison.Ordinal) || channel.Contains("انستاباي", StringComparison.Ordinal))
        {
            kind = FinancialAccountKind.InstaPay;
            return true;
        }

        return channel.Contains("كاش", StringComparison.Ordinal);
    }

    /// <summary>Creates the cash and wallet accounts when the database has none yet.</summary>
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
                accounts.TryAdd((FinancialAccountKind)reader.GetByte(1), reader.GetInt32(0));
        }

        foreach (var kind in new[] { FinancialAccountKind.Cash, FinancialAccountKind.VodafoneCash, FinancialAccountKind.InstaPay })
        {
            if (accounts.ContainsKey(kind)) continue;

            using var insert = CreateCommand(connection, transaction, @"
INSERT dbo.FinancialAccounts(AccountKind,Alias,Identifier,CurrentBalance,IsActive,IsDefault,Notes)
OUTPUT inserted.Id
VALUES(@Kind,@Alias,@Identifier,0,1,1,N'أنشئت تلقائيًا لمشتريات الشاشة');");
            insert.Parameters.Add("@Kind", SqlDbType.TinyInt).Value = (byte)kind;
            insert.Parameters.Add("@Alias", SqlDbType.NVarChar, 200).Value = kind switch
            {
                FinancialAccountKind.VodafoneCash => "فودافون كاش",
                FinancialAccountKind.InstaPay => "انستاباي",
                _ => "الخزنة الرئيسية"
            };
            insert.Parameters.Add("@Identifier", SqlDbType.NVarChar, 200).Value =
                kind == FinancialAccountKind.Cash ? "" : "غير محدد";
            accounts[kind] = (int)insert.ExecuteScalar()!;
        }

        return accounts;
    }

    private static string NextDocumentNumber(
        SqlConnection connection, SqlTransaction transaction, string documentType, int year, string prefix)
    {
        using var command = CreateCommand(connection, transaction, @"
IF NOT EXISTS (SELECT 1 FROM dbo.DocumentSequences WITH (UPDLOCK, HOLDLOCK)
               WHERE DocumentType = @Type AND SequenceYear = @Year)
    INSERT dbo.DocumentSequences(DocumentType,SequenceYear,LastValue) VALUES(@Type,@Year,0);
UPDATE dbo.DocumentSequences SET LastValue = LastValue + 1
WHERE DocumentType = @Type AND SequenceYear = @Year;
SELECT LastValue FROM dbo.DocumentSequences WHERE DocumentType = @Type AND SequenceYear = @Year;");
        command.Parameters.Add("@Type", SqlDbType.NVarChar, 30).Value = documentType;
        command.Parameters.Add("@Year", SqlDbType.SmallInt).Value = (short)year;
        var value = Convert.ToInt64(command.ExecuteScalar()!);
        return $"{prefix}-{year}-{value:0000}";
    }

    private static string BuildPaymentSummary(decimal cash, decimal vodafone, decimal instaPay)
    {
        var parts = new List<string>(3);
        if (cash > 0m) parts.Add($"نقدي {cash:0.00}");
        if (vodafone > 0m) parts.Add($"فودافون كاش {vodafone:0.00}");
        if (instaPay > 0m) parts.Add($"انستاباي {instaPay:0.00}");
        return parts.Count == 0 ? "بدون مبلغ" : string.Join(" + ", parts);
    }

    private void AddActivity(SqlConnection connection, SqlTransaction transaction, int invoiceId,
        string title, string details, decimal amount)
    {
        using var command = CreateCommand(connection, transaction, @"
INSERT dbo.PurchaseInvoiceActivities(PurchaseInvoiceId,OccurredAtUtc,ActionTitle,Details,Amount,ActorUserId)
VALUES(@Invoice,SYSUTCDATETIME(),@Title,@Details,@Amount,@Actor);");
        command.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
        command.Parameters.Add("@Title", SqlDbType.NVarChar, 200).Value = Trim(title, 200, "حركة فاتورة شراء");
        command.Parameters.Add("@Details", SqlDbType.NVarChar, 1500).Value = Trim(details, 1500, "");
        command.Parameters.Add("@Amount", SqlDbType.Decimal).Value = amount;
        command.Parameters["@Amount"].Precision = 19;
        command.Parameters["@Amount"].Scale = 2;
        command.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
        command.ExecuteNonQuery();
    }

    private void Audit(SqlConnection connection, SqlTransaction transaction, string action, int entityId,
        string details, string entityType)
    {
        using var command = CreateCommand(connection, transaction, @"
INSERT dbo.AuditLog(ActorUserId,Module,ActionName,EntityType,EntityId,NewValuesJson)
VALUES(@Actor,@Module,@Action,@EntityType,@EntityId,JSON_MODIFY(N'{}',N'$.details',@Details));");
        command.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
        command.Parameters.Add("@Module", SqlDbType.NVarChar, 100).Value = AuditModule;
        command.Parameters.Add("@Action", SqlDbType.NVarChar, 150).Value = action;
        command.Parameters.Add("@EntityType", SqlDbType.NVarChar, 100).Value = entityType;
        command.Parameters.Add("@EntityId", SqlDbType.NVarChar, 100).Value = entityId.ToString();
        command.Parameters.Add("@Details", SqlDbType.NVarChar, 1000).Value = Trim(details, 1000, "");
        command.ExecuteNonQuery();
    }

    private static PurchaseDataException Describe(Exception exception, string fallback)
    {
        if (exception is PurchaseDataException data) return data;
        if (exception is SqlException sql)
        {
            return sql.Number switch
            {
                2601 or 2627 => new PurchaseDataException("البيانات مكررة: رقم الفاتورة أو اسم المورد مسجل بالفعل. أعد المحاولة.", sql),
                547 => new PurchaseDataException("لا يمكن إتمام العملية لارتباط السجل بفاتورة أو حركة مخزون أخرى.", sql),
                1205 => new PurchaseDataException("تعارضت العملية مع مستخدم آخر. حدّث الشاشة ثم أعد المحاولة.", sql),
                2 => new PurchaseDataException("انتهت مهلة الاتصال بالخادم أثناء حفظ الفاتورة. تحقق من الشبكة ثم أعد المحاولة.", sql),
                2628 => new PurchaseDataException("أحد النصوص أطول من المسموح في قاعدة البيانات. اختصر اسم الصنف أو المورد أو الملاحظات.", sql),
                // SQL Server reports an undeclared command parameter as 137 (8178 can also occur
                // with RPC/parameter metadata faults). Show the exact fault, not a schema guess.
                137 or 8178 => new PurchaseDataException(
                    $"خطأ في معاملات أمر حفظ المشتريات: {sql.Message}", sql),
                // Missing table/column or parameter-metadata faults: show the schema object involved.
                208 or 207 or 8144 => new PurchaseDataException(
                    $"تعذر حفظ فاتورة الشراء: {sql.Message} — تحقق من تنفيذ sql/06_CreateFullSchema.sql ثم أعد المحاولة.", sql),
                229 or 230 => new PurchaseDataException(
                    "حساب الاتصال لا يملك صلاحية حفظ المشتريات. شغّل sql/12_EnablePurchasing.sql على الخادم ثم أعد المحاولة.", sql),
                >= 50000 => new PurchaseDataException(sql.Message, sql),
                // Never hide an unmapped SQL fault behind a message that cannot be diagnosed.
                _ => new PurchaseDataException($"{fallback} (SQL {sql.Number}: {sql.Message})", sql)
            };
        }

        return new PurchaseDataException(fallback, exception);
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
        try
        {
            connection.Open();
            // Purchases write Products/InventoryUnits, which carry filtered unique indexes.
            // Restore the required SET options on EVERY pooled checkout; a previous caller may
            // have left incompatible options on the same physical session (SQL Server 1934).
            using var options = connection.CreateCommand();
            options.CommandTimeout = 30;
            options.CommandText = @"
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;";
            options.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static SqlCommand CreateCommand(SqlConnection connection, SqlTransaction transaction, string sql) =>
        new(sql, connection, transaction) { CommandTimeout = 30 };

    private static DateTime AsLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    private sealed record CollectedPurchaseAmounts(
        decimal Cash, decimal Vodafone, decimal InstaPay,
        decimal RefundedCash, decimal RefundedVodafone, decimal RefundedInstaPay)
    {
        public decimal TotalPaid => Cash + Vodafone + InstaPay;
        public decimal TotalRefunded => RefundedCash + RefundedVodafone + RefundedInstaPay;
    }
}
