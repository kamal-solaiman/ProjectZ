using System.Collections.ObjectModel;
using System.Data;
using Microsoft.Data.SqlClient;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Data;

/// <summary>User-facing failure raised by the purchasing layer; the message is safe to show in the UI.</summary>
public sealed class PurchaseDataException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

public sealed record PurchaseLineRequest(int ProductId, int Quantity, decimal UnitPurchaseCost, int? LineId = null);

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

public sealed record PurchasePaymentRequest(int InvoiceId, byte[] Version, decimal CashPaid, decimal VodafonePaid, decimal InstaPayPaid, string Notes);
public sealed record PurchaseReturnLineInput(int LineId, int Quantity);
public sealed record PurchaseReturnRequest(int InvoiceId, byte[] Version, IReadOnlyList<PurchaseReturnLineInput> Lines, string Reason, bool PreferDamagedFirst);
public sealed record PurchaseReturnResult(string ReturnNumber, int ReturnedUnits, decimal ReturnedValue, decimal DebtReduction, decimal CreditIssued, PurchaseInvoiceStatus Status);
public sealed record PurchaseInvoiceEdit(int InvoiceId, byte[] Version, int SupplierId, string SupplierInvoiceNumber, DateTime IssuedAt,
    string Notes, decimal DiscountAmount, IReadOnlyList<PurchaseLineRequest> Lines);

/// <summary>SQL Server persistence for the purchase invoice screens and the supplier directory.</summary>
public sealed class PurchaseRepository(ConnectionSettingsProvider settings, int actorUserId)
{
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
       ISNULL(s.InstaPayAddress,N''), ISNULL(s.Notes,N''),
       (SELECT COUNT(*) FROM dbo.PurchaseInvoices WHERE SupplierId=s.Id AND InvoiceStatus<>3),
       (SELECT COALESCE(SUM(RemainingPayable),0) FROM dbo.vwPurchaseInvoiceSummary WHERE SupplierId=s.Id),
       (SELECT COALESCE(SUM(AvailableAmount),0) FROM dbo.vwSupplierCreditBalances WHERE SupplierId=s.Id)
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
                    Notes = reader.GetString(9), TotalPurchasesCount = reader.GetInt32(10),
                    OutstandingPayable = reader.GetDecimal(11), AvailableCredit = reader.GetDecimal(12)
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

                // Header snapshots belong to the posted document, not the live directory.
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
IF EXISTS(SELECT 1 FROM dbo.vwPurchaseInvoiceSummary WHERE SupplierId=@Id AND (RemainingPayable>0 OR AvailableCredit>0 OR NeedsReview=1))
    THROW 50257,N'لا يمكن أرشفة المورد قبل مطابقة وتسوية الدين والرصيد المتاح للمحل.',1;
SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.PurchaseInvoices WHERE SupplierId=@Id)
              OR EXISTS (SELECT 1 FROM dbo.InventoryUnits WHERE SupplierId=@Id)
              OR EXISTS (SELECT 1 FROM dbo.MaintenanceSpareParts WHERE SupplierId=@Id)
              OR EXISTS (SELECT 1 FROM dbo.MaintenanceSparePartMovements WHERE SupplierId=@Id)
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
            using var snapshot = connection.BeginTransaction(IsolationLevel.Serializable);
            using var command = connection.CreateCommand();
            command.Transaction = snapshot;
            command.CommandText = @"
SELECT i.Id, i.InvoiceNumber, ISNULL(i.SupplierInvoiceNumber,N''), i.SupplierId, i.SupplierNameSnapshot,
       i.SupplierPhoneSnapshot, i.SupplierCompanySnapshot, i.SupplierAddressSnapshot, i.IssuedAtUtc, i.InvoiceStatus,
       i.DiscountAmount, i.Notes, u.DisplayName, i.RowVersion, s.CreditApplied, s.NeedsReview,
       (SELECT COALESCE(SUM(r.NetReduction),0) FROM dbo.PurchaseReturns r WHERE r.PurchaseInvoiceId=i.Id)
FROM dbo.PurchaseInvoices i
JOIN dbo.Users u ON u.Id = i.BuyerUserId
JOIN dbo.vwPurchaseInvoiceSummary s ON s.PurchaseInvoiceId=i.Id
ORDER BY i.IssuedAtUtc DESC, i.Id DESC;
SELECT l.PurchaseInvoiceId, l.Id, l.LineNumber, l.ProductId, ISNULL(l.BarcodeSnapshot,N''), l.ProductNameSnapshot,
       ISNULL(l.CategoryNameSnapshot,N''), l.UnitNameSnapshot, l.TrackSerialNumber, l.Quantity, l.ReturnedQuantity,
       l.UnitPurchaseCost
FROM dbo.PurchaseInvoiceLines l
ORDER BY l.PurchaseInvoiceId, l.LineNumber;
SELECT lu.PurchaseInvoiceLineId, lu.InventoryUnitId, COALESCE(iu.SerialNumber,N'')
FROM dbo.PurchaseInvoiceLineUnits lu
JOIN dbo.InventoryUnits iu ON iu.Id = lu.InventoryUnitId AND iu.UnitStatus<>3;
SELECT p.PurchaseInvoiceId, a.AccountKind, p.PaymentKind, p.Amount, p.PaidAtUtc, a.Alias, a.Id, p.Note, p.Id
FROM dbo.PurchasePayments p
JOIN dbo.FinancialAccounts a ON a.Id = p.FinancialAccountId;
SELECT a.PurchaseInvoiceId, a.OccurredAtUtc, a.ActionTitle, a.Details, u.DisplayName, a.Amount, a.DocumentDelta
FROM dbo.PurchaseInvoiceActivities a
JOIN dbo.Users u ON u.Id = a.ActorUserId
ORDER BY a.Id;
SELECT SourceInvoiceId,Id,SupplierId,ReturnNumber,Amount,AppliedAmount,ReceivedAmount,AvailableAmount,RowVersion FROM dbo.vwSupplierCreditBalances ORDER BY Id;
SELECT a.InvoiceId,a.AppliedAtUtc,c.ReturnNumber,a.Amount,c.Id FROM dbo.SupplierCreditApplications a JOIN dbo.vwSupplierCreditBalances c ON c.Id=a.CreditId ORDER BY a.Id;";

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
                    Notes = reader.GetString(11), DatabaseRowVersion = (byte[])reader[13],
                    CreditApplied = reader.GetDecimal(14), NeedsReview = reader.GetBoolean(15), DocumentedReturnValue = reader.GetDecimal(16)
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
                if (!byId.TryGetValue(invoiceId, out var invoice)) continue;

                var kind = reader.GetByte(1);
                var paymentKind = reader.GetByte(2);
                var amount = reader.GetDecimal(3);

                var label = paymentKind == 2 ? "قبض من المورد" : "سداد للمورد";
                invoice.Postings.Add(new PurchaseStatementPosting(AsLocal(reader.GetDateTime(4)),
                    $"{label} #{reader.GetInt32(8)} · {reader.GetString(5)} (#{reader.GetInt32(6)}) · {reader.GetString(7)}",
                    paymentKind == 2 ? amount : -amount));
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
                    Amount = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                    DocumentDelta = reader.IsDBNull(6) ? null : reader.GetDecimal(6)
                });
            }

            reader.NextResult();
            while (reader.Read())
                byId[reader.GetInt32(0)].CreditNotes.Add(new SupplierCreditItem(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(0),
                    reader.GetString(3), reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7), (byte[])reader[8]));
            reader.NextResult();
            while (reader.Read())
                byId[reader.GetInt32(0)].Postings.Add(new PurchaseStatementPosting(AsLocal(reader.GetDateTime(1)),
                    $"مقاصة من إشعار #{reader.GetInt32(4)} · {reader.GetString(2)} · {reader.GetDecimal(3):0.00} (نقل بين مستندين، دون قبض أو صرف)", 0m));
            reader.Close();
            snapshot.Commit();

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

    public SavedPurchase CreateInvoice(PurchaseInvoiceRequest r) => Post("CreatePurchaseDocument", c =>
    {
        BindDocument(c, r.SupplierId, r.SupplierInvoiceNumber, r.IssuedAt, r.DiscountAmount, r.Notes, r.Lines);
        BindTender(c, r.CashPaid, r.VodafonePaid, r.InstaPayPaid);
    }, c =>
    {
        using var row = c.ExecuteReader();
        if (!row.Read()) throw new PurchaseDataException("لم يُرجع الخادم نتيجة الحفظ. حدّث السجل قبل أي محاولة أخرى.");
        return new SavedPurchase(row.GetInt32(0), row.GetString(1), row.GetDecimal(2), row.GetDecimal(3), row.GetDecimal(4), (PurchaseInvoiceStatus)row.GetByte(5));
    });

    public void UpdateInvoice(PurchaseInvoiceEdit r) => Post("EditPurchaseDocument", c =>
    {
        BindVersion(c, r.InvoiceId, r.Version);
        BindDocument(c, r.SupplierId, r.SupplierInvoiceNumber, r.IssuedAt, r.DiscountAmount, r.Notes, r.Lines);
    }, c => c.ExecuteNonQuery());

    public void PayInvoice(PurchasePaymentRequest r) => Post("PayPurchaseDocument", c =>
    {
        BindVersion(c, r.InvoiceId, r.Version);
        BindTender(c, r.CashPaid, r.VodafonePaid, r.InstaPayPaid);
        Text(c, "@Notes", 1000, r.Notes);
    }, c => c.ExecuteNonQuery());

    public PurchaseReturnResult ReturnToSupplier(PurchaseReturnRequest r) => PostReturn(r, false);
    public PurchaseReturnResult CancelInvoice(int id, byte[] version) => PostReturn(new(id, version, [], "إلغاء فاتورة الشراء", false), true);

    private PurchaseReturnResult PostReturn(PurchaseReturnRequest r, bool cancel) => Post("ReturnPurchaseDocument", c =>
    {
        BindVersion(c, r.InvoiceId, r.Version);
        var table = new DataTable();
        table.Columns.Add("LineId", typeof(int)); table.Columns.Add("Quantity", typeof(int));
        foreach (var line in r.Lines) table.Rows.Add(line.LineId, line.Quantity);
        Rows(c, "@Lines", "dbo.PurchaseReturnSelection", table);
        c.Parameters.Add("@Cancel", SqlDbType.Bit).Value = cancel;
        c.Parameters.Add("@DamagedFirst", SqlDbType.Bit).Value = r.PreferDamagedFirst;
        Text(c, "@Reason", 1000, r.Reason);
    }, c =>
    {
        using var row = c.ExecuteReader();
        if (!row.Read()) throw new PurchaseDataException("تعذرت قراءة نتيجة المرتجع. حدّث السجل.");
        return new PurchaseReturnResult(row.GetString(0), row.GetInt32(1), row.GetDecimal(2), row.GetDecimal(3), row.GetDecimal(4), (PurchaseInvoiceStatus)row.GetByte(5));
    });

    public void DeleteInvoice(int id, byte[] version) => Post("DeleteUnsettledPurchase", c => BindVersion(c, id, version), c => c.ExecuteNonQuery());

    public void ApplyCredit(int id, byte[] version, SupplierCreditItem note, decimal amount) => Post("ApplySupplierCredit", c =>
        BindCredit(c, id, version, note, amount), c => c.ExecuteNonQuery());

    public void ReceiveCredit(int id, byte[] version, SupplierCreditItem note, decimal amount, int accountId, string evidence) => Post("ReceiveSupplierCredit", c =>
    {
        BindCredit(c, id, version, note, amount);
        c.Parameters.Add("@Account", SqlDbType.Int).Value = accountId;
        Text(c, "@Evidence", 120, evidence);
    }, c => c.ExecuteNonQuery());

    public List<PurchaseReceiptAccount> LoadReceiptAccounts()
    {
        try
        {
            using var connection = OpenConnection();
            using var c = connection.CreateCommand();
            c.CommandText = "SELECT Id,Alias,Identifier FROM dbo.FinancialAccounts WHERE IsActive=1 ORDER BY AccountKind,Id;";
            using var reader = c.ExecuteReader();
            var accounts = new List<PurchaseReceiptAccount>();
            while (reader.Read()) accounts.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
            return accounts;
        }
        catch (SqlException ex) { throw Describe(ex, "تعذر تحميل حسابات القبض."); }
    }

    private T Post<T>(string name, Action<SqlCommand> bind, Func<SqlCommand, T> execute)
    {
        try
        {
            using var connection = OpenConnection();
            using var c = new SqlCommand("dbo." + name, connection) { CommandType = CommandType.StoredProcedure, CommandTimeout = 30 };
            c.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
            bind(c);
            return execute(c);
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not PurchaseDataException))
        { throw Describe(ex, "تعذرت تسوية مستند المشتريات."); }
    }

    private static void BindVersion(SqlCommand c, int id, byte[] version)
    {
        c.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        Version(c, "@Version", version);
    }

    private static void BindCredit(SqlCommand c, int id, byte[] version, SupplierCreditItem note, decimal amount)
    {
        BindVersion(c, id, version);
        c.Parameters.Add("@Credit", SqlDbType.Int).Value = note.Id;
        Version(c, "@CreditVersion", note.Version);
        Money(c, "@Amount", amount);
    }

    private static void Version(SqlCommand c, string name, byte[] value)
    {
        if (value is not { Length: 8 }) throw new PurchaseDataException("حدّث المستند قبل التسوية.");
        c.Parameters.Add(name, SqlDbType.VarBinary, 8).Value = value;
    }

    private static void BindDocument(SqlCommand c, int supplier, string reference, DateTime date, decimal discount, string notes, IReadOnlyList<PurchaseLineRequest> lines)
    {
        c.Parameters.Add("@Supplier", SqlDbType.Int).Value = supplier;
        c.Parameters.Add("@Date", SqlDbType.DateTime2).Value = date.ToUniversalTime();
        Text(c, "@Reference", 100, reference); Text(c, "@Notes", 1000, notes); Money(c, "@Discount", discount);
        var t = new DataTable();
        t.Columns.Add("Ordinal", typeof(int)); t.Columns.Add("LineId", typeof(int)); t.Columns.Add("ProductId", typeof(int));
        t.Columns.Add("Quantity", typeof(int)); t.Columns.Add("Cost", typeof(decimal));
        foreach (var l in lines) t.Rows.Add(t.Rows.Count + 1, (object?)l.LineId ?? DBNull.Value, l.ProductId, l.Quantity, Cents(l.UnitPurchaseCost));
        Rows(c, "@Items", "dbo.PurchaseItemSelection", t);
    }

    private static void BindTender(SqlCommand c, decimal cash, decimal vodafone, decimal instaPay)
    { Money(c, "@Cash", cash); Money(c, "@Vodafone", vodafone); Money(c, "@InstaPay", instaPay); }

    private static void Rows(SqlCommand c, string name, string type, DataTable rows)
    {
        var p = c.Parameters.Add(name, SqlDbType.Structured);
        p.TypeName = type; p.Value = rows;
    }

    private static decimal Cents(decimal value) => decimal.Round(value, 2) == value ? value : throw new PurchaseDataException("المبالغ تقبل منزلتين عشريتين فقط.");
    private static void Money(SqlCommand c, string name, decimal value)
    {
        var p = c.Parameters.Add(name, SqlDbType.Decimal); p.Precision = 19; p.Scale = 2; p.Value = Cents(value);
    }

    private static void Text(SqlCommand c, string name, int max, string value)
    {
        value = value.Trim();
        if (value.Length > max) throw new PurchaseDataException($"النص أطول من الحد المسموح ({max} حرف).");
        c.Parameters.Add(name, SqlDbType.NVarChar, max).Value = value;
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
                -2 or 2 => new PurchaseDataException("انتهت مهلة الاتصال بالخادم أثناء حفظ الفاتورة. تحقق من الشبكة وحدّث السجل للتحقق من النتيجة قبل تكرار العملية.", sql),
                2628 => new PurchaseDataException("أحد النصوص أطول من المسموح في قاعدة البيانات. اختصر اسم الصنف أو المورد أو الملاحظات.", sql),
                // SQL Server reports an undeclared command parameter as 137 (8178 can also occur
                // with RPC/parameter metadata faults). Show the exact fault, not a schema guess.
                137 or 8178 => new PurchaseDataException(
                    $"خطأ في معاملات أمر حفظ المشتريات: {sql.Message}", sql),
                // Missing table/column or parameter-metadata faults: show the schema object involved.
                208 or 207 or 8144 or 2812 or 2715 => new PurchaseDataException(
                    $"تعذر حفظ فاتورة الشراء: {sql.Message} — تحقق من تنفيذ sql/07_CreateReportingViews.sql ثم sql/25_EnablePurchaseSettlement.sql وتوافق إصدار العميل.", sql),
                229 or 230 => new PurchaseDataException(
                    "حساب الاتصال لا يملك صلاحية حفظ المشتريات. تحقق من sql/12_EnablePurchasing.sql ثم sql/25_EnablePurchaseSettlement.sql، دون منح كتابة مباشرة.", sql),
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
}
