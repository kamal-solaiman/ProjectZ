using System.Collections.ObjectModel;
using System.Data;
using Microsoft.Data.SqlClient;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Data;

/// <summary>User-facing failure raised by the POS sales layer; the message is safe to show in the UI.</summary>
public sealed class SalesDataException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

// ExpectedPrice is a stale-quote check, not a posting price. SQL supplies price, cost and snapshots.
public sealed record SaleItemRequest(int ProductId, int Quantity, PriceTier Tier, decimal ExpectedPrice);
public sealed record PosSaleRequest(int? CustomerId, bool IsWalkIn, PriceTier PriceTier, decimal DiscountAmount,
    IReadOnlyList<SaleItemRequest> Lines, decimal CashPaid, decimal VodafonePaid, decimal InstaPayPaid, string Notes);

public sealed record SavedSale(int InvoiceId, string InvoiceNumber, decimal NetTotal, decimal PaidTotal, bool IsCredit);

/// <summary>SQL Server persistence for the POS sale, the customer directory and saved sales invoices.</summary>
public sealed class SalesRepository(ConnectionSettingsProvider settings, int actorUserId)
{
    // ---------------------------------------------------------------- customers

    public void LoadCustomersInto(ObservableCollection<PosCustomerDesignItem> target)
    {
        var loaded = new List<PosCustomerDesignItem>();
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT Id, Name, ISNULL(Phone,N''), ISNULL(SecondaryPhone,N''), Address, Notes, DefaultPriceTier, IsWalkIn
FROM dbo.Customers
WHERE IsActive = 1 AND IsWalkIn = 0
ORDER BY Name;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                loaded.Add(new PosCustomerDesignItem
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Phone = reader.GetString(2),
                    SecondaryPhone = reader.GetString(3),
                    Address = reader.GetString(4),
                    Notes = reader.GetString(5),
                    DefaultPriceTier = (PriceTier)reader.GetByte(6),
                    IsWalkIn = reader.GetBoolean(7)
                });
            }
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            throw Describe(ex, "تعذر تحميل دليل العملاء من قاعدة البيانات.");
        }

        // CK_Customers_WalkIn keeps the walk-in phone NULL, so the seeded walk-in row is always rebuilt here.
        var walkIn = target.FirstOrDefault(c => c.IsWalkIn)
            ?? new PosCustomerDesignItem { Id = 0, Name = "عميل نقدي (عام)", IsWalkIn = true };

        target.Clear();
        target.Add(walkIn);
        foreach (var customer in loaded)
            target.Add(customer);
    }

    /// <summary>
    /// Database-backed duplicate check for the customer dialogs; it mirrors the UX_Customers_Phone
    /// filtered unique index, which is the real guard. Returns false on any failure: a validation
    /// helper must never crash the dialog, and the INSERT still reports the duplicate clearly.
    /// </summary>
    public bool PhoneExists(string phone, int? excludeCustomerId = null)
    {
        var trimmed = (phone ?? "").Trim();
        if (trimmed.Length == 0) return false;

        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM dbo.Customers
    WHERE Phone = @Phone AND (@Exclude IS NULL OR Id <> @Exclude)) THEN 1 ELSE 0 END;";
            command.Parameters.Add("@Phone", SqlDbType.NVarChar, 30).Value = trimmed;
            command.Parameters.Add("@Exclude", SqlDbType.Int).Value = (object?)excludeCustomerId ?? DBNull.Value;
            return Convert.ToInt32(command.ExecuteScalar() ?? 0) == 1;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public PosCustomerDesignItem CreateCustomer(
        string name, string phone, string secondaryPhone, string address, string notes, PriceTier defaultPriceTier)
    {
        name = name.Trim();
        phone = phone.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new SalesDataException("يرجى إدخال اسم العميل.");
        if (string.IsNullOrWhiteSpace(phone)) throw new SalesDataException("يرجى إدخال رقم هاتف العميل.");
        if (name.Length > 200) throw new SalesDataException("اسم العميل يجب ألا يتجاوز 200 حرف.");
        if (phone.Length > 30) throw new SalesDataException("رقم الهاتف يجب ألا يتجاوز 30 حرفًا.");
        if (secondaryPhone.Trim().Length > 30) throw new SalesDataException("الهاتف الإضافي يجب ألا يتجاوز 30 حرفًا.");
        if (address.Trim().Length > 500) throw new SalesDataException("العنوان يجب ألا يتجاوز 500 حرف.");
        if (notes.Trim().Length > 1000) throw new SalesDataException("الملاحظات يجب ألا تتجاوز 1000 حرف.");

        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT dbo.Customers(Name,Phone,SecondaryPhone,Address,Notes,DefaultPriceTier,IsWalkIn)
OUTPUT inserted.Id
VALUES(@Name,@Phone,@Secondary,@Address,@Notes,@Tier,0);";
            command.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value = name;
            command.Parameters.Add("@Phone", SqlDbType.NVarChar, 30).Value = phone;
            command.Parameters.Add("@Secondary", SqlDbType.NVarChar, 30).Value = secondaryPhone.Trim();
            command.Parameters.Add("@Address", SqlDbType.NVarChar, 500).Value = address.Trim();
            command.Parameters.Add("@Notes", SqlDbType.NVarChar, 1000).Value = notes.Trim();
            command.Parameters.Add("@Tier", SqlDbType.TinyInt).Value = (byte)defaultPriceTier;
            var id = (int)command.ExecuteScalar()!;

            return new PosCustomerDesignItem
            {
                Id = id,
                Name = name,
                Phone = phone,
                SecondaryPhone = secondaryPhone.Trim(),
                Address = address.Trim(),
                Notes = notes.Trim(),
                DefaultPriceTier = defaultPriceTier,
                IsWalkIn = false
            };
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            throw new SalesDataException("رقم الهاتف مسجل بالفعل لعميل آخر. أدخل رقمًا مختلفًا أو ابحث عن العميل الحالي.", ex);
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            throw Describe(ex, "تعذر حفظ بيانات العميل في قاعدة البيانات.");
        }
    }

    public void UpdateCustomer(PosCustomerDesignItem customer)
    {
        if (customer.Id <= 0) throw new SalesDataException("لا يمكن تعديل عميل غير محفوظ في قاعدة البيانات.");
        if (customer.IsWalkIn) throw new SalesDataException("العميل النقدي (العام) هو حساب افتراضي ثابت ولا يمكن تعديله.");

        var name = customer.Name.Trim();
        var phone = customer.Phone.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new SalesDataException("يرجى إدخال اسم العميل.");
        if (string.IsNullOrWhiteSpace(phone)) throw new SalesDataException("يرجى إدخال رقم هاتف العميل.");
        if (name.Length > 200) throw new SalesDataException("اسم العميل يجب ألا يتجاوز 200 حرف.");
        if (phone.Length > 30) throw new SalesDataException("رقم الهاتف يجب ألا يتجاوز 30 حرفًا.");
        if (customer.SecondaryPhone.Trim().Length > 30) throw new SalesDataException("الهاتف الإضافي يجب ألا يتجاوز 30 حرفًا.");
        if (customer.Address.Trim().Length > 500) throw new SalesDataException("العنوان يجب ألا يتجاوز 500 حرف.");
        if (customer.Notes.Trim().Length > 1000) throw new SalesDataException("الملاحظات يجب ألا تتجاوز 1000 حرف.");

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                using (var command = CreateCommand(connection, transaction, @"
UPDATE dbo.Customers
SET Name=@Name, Phone=@Phone, SecondaryPhone=@Secondary, Address=@Address, Notes=@Notes,
    DefaultPriceTier=@Tier, UpdatedAtUtc=SYSUTCDATETIME()
WHERE Id=@Id;"))
                {
                    command.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value = name;
                    command.Parameters.Add("@Phone", SqlDbType.NVarChar, 30).Value = phone;
                    command.Parameters.Add("@Secondary", SqlDbType.NVarChar, 30).Value = customer.SecondaryPhone.Trim();
                    command.Parameters.Add("@Address", SqlDbType.NVarChar, 500).Value = customer.Address.Trim();
                    command.Parameters.Add("@Notes", SqlDbType.NVarChar, 1000).Value = customer.Notes.Trim();
                    command.Parameters.Add("@Tier", SqlDbType.TinyInt).Value = (byte)customer.DefaultPriceTier;
                    command.Parameters.Add("@Id", SqlDbType.Int).Value = customer.Id;
                    if (command.ExecuteNonQuery() != 1)
                        throw new SalesDataException("العميل غير موجود في قاعدة البيانات؛ حدّث شاشة العملاء ثم أعد المحاولة.");
                }

                // Snapshots on already-issued invoices stay as captured at sale time; only dbo.Customers is updated.
                Audit(connection, transaction, "UpdateCustomer", customer.Id,
                    $"تعديل بيانات العميل {name}", "Customer");
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            throw new SalesDataException("رقم الهاتف مسجل بالفعل لعميل آخر. أدخل رقمًا مختلفًا أو ابحث عن العميل الحالي.", ex);
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not SalesDataException))
        {
            throw Describe(ex, "تعذر حفظ تعديلات العميل في قاعدة البيانات.");
        }
    }

    /// <summary>Deletes a customer with no history; archives (IsActive=0) one that has invoices or orders.</summary>
    public bool DeleteCustomer(int customerId)
    {
        if (customerId <= 0) throw new SalesDataException("لا يمكن حذف العميل النقدي (العام).");

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                bool hasHistory;
                using (var check = CreateCommand(connection, transaction, @"
SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.SalesInvoices WHERE CustomerId=@Id)
              OR EXISTS (SELECT 1 FROM dbo.MaintenanceOrders WHERE CustomerId=@Id)
            THEN 1 ELSE 0 END;"))
                {
                    check.Parameters.Add("@Id", SqlDbType.Int).Value = customerId;
                    hasHistory = Convert.ToInt32(check.ExecuteScalar()) == 1;
                }

                if (hasHistory)
                {
                    using var archive = CreateCommand(connection, transaction,
                        "UPDATE dbo.Customers SET IsActive=0, UpdatedAtUtc=SYSUTCDATETIME() WHERE Id=@Id;");
                    archive.Parameters.Add("@Id", SqlDbType.Int).Value = customerId;
                    archive.ExecuteNonQuery();
                    Audit(connection, transaction, "ArchiveCustomer", customerId,
                        "أرشفة العميل بعد وجود فواتير بيع أو تذاكر صيانة", "Customer");
                    transaction.Commit();
                    return false;
                }

                using (var delete = CreateCommand(connection, transaction, "DELETE dbo.Customers WHERE Id=@Id;"))
                {
                    delete.Parameters.Add("@Id", SqlDbType.Int).Value = customerId;
                    delete.ExecuteNonQuery();
                }

                Audit(connection, transaction, "DeleteCustomer", customerId, "حذف عميل بدون سجل مبيعات أو صيانة", "Customer");
                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not SalesDataException))
        {
            throw Describe(ex, "تعذر حذف العميل من قاعدة البيانات.");
        }
    }

    // ------------------------------------------------------------------- sales

    public SavedSale CompleteSale(PosSaleRequest request) => Post("dbo.IssueSale", command =>
    {
        command.Parameters.Add("@Customer", SqlDbType.Int).Value = (object?)request.CustomerId ?? DBNull.Value;
        command.Parameters.Add("@WalkIn", SqlDbType.Bit).Value = request.IsWalkIn;
        command.Parameters.Add("@Tier", SqlDbType.TinyInt).Value = TierNumber(request.PriceTier);
        AddMoney(command, "@Discount", request.DiscountAmount);
        AddItems(command, request.Lines);
        AddTender(command, request.CashPaid, request.VodafonePaid, request.InstaPayPaid);
        command.Parameters.Add("@Notes", SqlDbType.NVarChar, 1000).Value = Trim(request.Notes, 1000, "");
        using var reader = command.ExecuteReader();
        reader.Read();
        var net = reader.GetDecimal(2);
        var paid = reader.GetDecimal(3);
        return new SavedSale(reader.GetInt32(0), reader.GetString(1), net, paid, paid < net);
    });

    public void CollectPayment(SalesInvoiceDesignItem invoice, decimal cash, decimal vodafone, decimal instaPay, string note) =>
        Post("dbo.CollectSalePayment", command =>
        {
            AddInvoiceVersion(command, invoice);
            AddTender(command, cash, vodafone, instaPay);
            command.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value = Trim(note, 1000, "تحصيل دفعة متبقية");
            return command.ExecuteNonQuery();
        });

    // ----------------------------------------------------------------- invoices

    public void LoadInvoicesInto(ObservableCollection<SalesInvoiceDesignItem> invoices)
    {
        var headers = new List<SalesInvoiceDesignItem>();
        var byId = new Dictionary<int, SalesInvoiceDesignItem>();

        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT si.Id, si.InvoiceNumber, si.CustomerId, si.CustomerNameSnapshot, si.CustomerPhoneSnapshot,
       si.CustomerAddressSnapshot, si.IsWalkInCustomer, si.PriceTier, si.IssuedAtUtc, si.InvoiceStatus,
       si.DiscountAmount, si.Notes, u.DisplayName, si.RowVersion
FROM dbo.SalesInvoices si
JOIN dbo.Users u ON u.Id = si.CashierUserId
ORDER BY si.IssuedAtUtc DESC, si.Id DESC;
SELECT l.SalesInvoiceId, l.Id, l.LineNumber, l.ProductId, l.BarcodeSnapshot, l.ProductNameSnapshot,
       l.CategoryNameSnapshot, l.UnitNameSnapshot, l.TrackSerialNumber, l.PriceTier, l.Quantity,
       l.ReturnedQuantity, l.UnitPrice, l.UnitCost, l.IsExchangeReplacement, v.NetLineAmount
FROM dbo.SalesInvoiceLines l JOIN dbo.vwSalesLineValuation v ON v.Id=l.Id
ORDER BY l.SalesInvoiceId, l.LineNumber;
SELECT lu.SalesInvoiceLineId, lu.InventoryUnitId, COALESCE(iu.SerialNumber,N''), lu.AllocationStatus
FROM dbo.SalesInvoiceLineUnits lu
JOIN dbo.InventoryUnits iu ON iu.Id = lu.InventoryUnitId;
SELECT p.SalesInvoiceId, a.AccountKind, a.Id, a.Alias,
       SUM(CASE WHEN p.PaymentKind=1 THEN p.Amount ELSE 0 END), SUM(CASE WHEN p.PaymentKind=2 THEN p.Amount ELSE 0 END)
FROM dbo.SalesPayments p JOIN dbo.FinancialAccounts a ON a.Id=p.FinancialAccountId
GROUP BY p.SalesInvoiceId,a.AccountKind,a.Id,a.Alias ORDER BY p.SalesInvoiceId,a.AccountKind,a.Id;
SELECT a.SalesInvoiceId, a.OccurredAtUtc, a.ActionTitle, a.Details, u.DisplayName, a.Amount
FROM dbo.SalesInvoiceActivities a
JOIN dbo.Users u ON u.Id = a.ActorUserId
ORDER BY a.Id;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var invoice = new SalesInvoiceDesignItem
                {
                    Id = reader.GetInt32(0),
                    InvoiceNumber = reader.GetString(1),
                    CustomerId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    CustomerName = reader.GetString(3),
                    CustomerPhone = reader.GetString(4),
                    CustomerAddress = reader.GetString(5),
                    IsWalkInCustomer = reader.GetBoolean(6),
                    IssuedAt = AsLocal(reader.GetDateTime(8)),
                    CashierName = reader.GetString(12),
                    DiscountAmount = reader.GetDecimal(10),
                    Status = (SalesInvoiceStatus)reader.GetByte(9),
                    DatabaseRowVersion = (byte[])reader[13]
                };
                invoice.DefaultPriceTier = (PriceTier)reader.GetByte(7);
                invoice.Notes = reader.GetString(11);

                headers.Add(invoice);
                byId[invoice.Id] = invoice;
            }

            var linesById = new Dictionary<int, SalesInvoiceLineDesignItem>();
            reader.NextResult();
            while (reader.Read())
            {
                if (!byId.TryGetValue(reader.GetInt32(0), out var invoice)) continue;

                var line = new SalesInvoiceLineDesignItem
                {
                    DatabaseLineId = reader.GetInt32(1),
                    LineNumber = reader.GetInt32(2),
                    ProductId = reader.GetInt32(3),
                    Barcode = reader.GetString(4),
                    ProductName = reader.GetString(5),
                    Category = reader.GetString(6),
                    Unit = reader.GetString(7),
                    TrackSerialNumber = reader.GetBoolean(8),
                    PriceTier = (PriceTier)reader.GetByte(9),
                    Quantity = reader.GetInt32(10),
                    UnitPrice = reader.GetDecimal(12),
                    UnitCost = reader.GetDecimal(13),
                    IsExchangeReplacement = reader.GetBoolean(14),
                    SavedNetAmount = reader.GetDecimal(15)
                };

                invoice.Items.Add(line);
                linesById[line.DatabaseLineId] = line;

                var returned = reader.GetInt32(11);
                if (returned > 0) line.ReturnedQuantity = returned;
            }

            reader.NextResult();
            while (reader.Read())
            {
                var lineId = reader.GetInt32(0);
                if (!linesById.TryGetValue(lineId, out var line)) continue;

                var unitId = reader.GetInt32(1);
                var serial = reader.GetString(2);
                if (!line.UnitRegistrationIds.Contains(unitId)) line.UnitRegistrationIds.Add(unitId);
                if (!string.IsNullOrWhiteSpace(serial)) line.SerialNumbers.Add(serial);
            }

            reader.NextResult();
            while (reader.Read())
            {
                var invoiceId = reader.GetInt32(0);
                if (!byId.TryGetValue(invoiceId, out var invoice)) continue;
                var collected = reader.GetDecimal(4);
                var refunded = reader.GetDecimal(5);
                switch ((FinancialAccountKind)reader.GetByte(1))
                {
                    case FinancialAccountKind.Cash: invoice.CashPaid += collected; break;
                    case FinancialAccountKind.VodafoneCash: invoice.VodafoneCashPaid += collected; break;
                    case FinancialAccountKind.InstaPay: invoice.InstaPayPaid += collected; break;
                }
                invoice.RefundedAmount += refunded;
                invoice.PayingAccounts.Add(new(reader.GetInt32(2), reader.GetString(3), collected - refunded));
            }

            reader.NextResult();
            while (reader.Read())
            {
                var invoiceId = reader.GetInt32(0);
                if (!byId.TryGetValue(invoiceId, out var invoice)) continue;

                invoice.ActivityLog.Add(new SalesInvoiceActivityEntry
                {
                    Timestamp = AsLocal(reader.GetDateTime(1)),
                    ActionTitle = reader.GetString(2),
                    Details = reader.GetString(3),
                    ActorName = reader.GetString(4),
                    Amount = reader.GetDecimal(5)
                });
            }

        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            throw Describe(ex, "تعذر تحميل فواتير البيع من قاعدة البيانات.");
        }

        invoices.Clear();
        foreach (var invoice in headers)
            invoices.Add(invoice);
    }

    public void UpdateNotes(SalesInvoiceDesignItem invoice, string notes) => Post("dbo.UpdateSaleNotes", command =>
    {
        AddInvoiceVersion(command, invoice);
        command.Parameters.Add("@Notes", SqlDbType.NVarChar, 1000).Value = Trim(notes, 1000, "");
        return command.ExecuteNonQuery();
    });

    public sealed record ReturnLineRequest(int SalesInvoiceLineId, int GoodQuantity, int DamagedQuantity);
    public sealed record ReturnRequest(SalesInvoiceDesignItem Invoice, string Reason, IReadOnlyList<ReturnLineRequest> Lines);
    public sealed record ExchangeRequest(SalesInvoiceDesignItem Invoice, string Reason, IReadOnlyList<ReturnLineRequest> Lines,
        IReadOnlyList<SaleItemRequest> Replacements);
    public sealed record ExchangePayment(decimal Cash, decimal Vodafone, decimal InstaPay);
    public sealed record AdjustmentReceipt(string Number, string Settlement);

    private enum Adjustment : byte { Return, Exchange, Cancel }

    public AdjustmentReceipt CreateReturn(ReturnRequest request) =>
        Adjust(Adjustment.Return, request.Invoice, request.Reason, request.Lines, [], new(0, 0, 0));
    public AdjustmentReceipt CreateExchange(ExchangeRequest request, ExchangePayment payment) =>
        Adjust(Adjustment.Exchange, request.Invoice, request.Reason, request.Lines, request.Replacements, payment);
    public AdjustmentReceipt CancelSale(SalesInvoiceDesignItem invoice) =>
        Adjust(Adjustment.Cancel, invoice, "إلغاء فاتورة بيع", [], [], new(0, 0, 0));

    private AdjustmentReceipt Adjust(Adjustment mode, SalesInvoiceDesignItem invoice, string reason,
        IReadOnlyList<ReturnLineRequest> lines, IReadOnlyList<SaleItemRequest> items, ExchangePayment payment) =>
        Post("dbo.AdjustSale", command =>
        {
            AddInvoiceVersion(command, invoice);
            command.Parameters.Add("@Mode", SqlDbType.TinyInt).Value = (byte)mode;
            command.Parameters.Add("@Reason", SqlDbType.NVarChar, 1000).Value = Trim(reason, 1000, "");
            var returns = new DataTable();
            returns.Columns.Add("LineId", typeof(int));
            returns.Columns.Add("Good", typeof(int));
            returns.Columns.Add("Damaged", typeof(int));
            foreach (var line in lines) returns.Rows.Add(line.SalesInvoiceLineId, line.GoodQuantity, line.DamagedQuantity);
            AddTable(command, "@Returns", "dbo.SaleReturnSelection", returns);
            AddItems(command, items);
            AddTender(command, payment.Cash, payment.Vodafone, payment.InstaPay);
            using var reader = command.ExecuteReader();
            reader.Read();
            return new AdjustmentReceipt(reader.GetString(0), reader.GetString(1));
        });

    private T Post<T>(string procedure, Func<SqlCommand, T> execute)
    {
        try
        {
            using var connection = OpenConnection();
            using var command = new SqlCommand(procedure, connection) { CommandType = CommandType.StoredProcedure };
            command.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
            return execute(command);
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not SalesDataException))
        {
            throw Describe(ex, "تعذر تأكيد نتيجة العملية. حدّث الفاتورة وراجع السجل قبل إعادة المحاولة.");
        }
    }

    private static void AddInvoiceVersion(SqlCommand command, SalesInvoiceDesignItem invoice)
    {
        if (invoice.Id <= 0 || invoice.DatabaseRowVersion is not { Length: 8 })
            throw new SalesDataException("حدّث الفواتير وأعد فتح العملية للحصول على النسخة المحفوظة.");
        command.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoice.Id;
        command.Parameters.Add("@ExpectedVersion", SqlDbType.Binary, 8).Value = invoice.DatabaseRowVersion;
    }

    private static void AddTender(SqlCommand command, decimal cash, decimal vodafone, decimal instaPay)
    {
        AddMoney(command, "@Cash", cash);
        AddMoney(command, "@Vodafone", vodafone);
        AddMoney(command, "@InstaPay", instaPay);
    }

    private static void ValidateMoney(decimal value)
    {
        if (value < 0m || value > 99999999999999999.99m || decimal.Round(value, 2) != value)
            throw new SalesDataException("أدخل مبلغًا غير سالب بدقّة القرش.");
    }

    private static void AddMoney(SqlCommand command, string name, decimal value)
    {
        ValidateMoney(value);
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = 19;
        parameter.Scale = 2;
        parameter.Value = value;
    }

    private static byte TierNumber(PriceTier tier) => Enum.IsDefined(tier) ? (byte)tier : throw new SalesDataException("فئة السعر غير صحيحة.");

    private static void AddItems(SqlCommand command, IReadOnlyList<SaleItemRequest> items)
    {
        var table = new DataTable();
        table.Columns.Add("Ordinal", typeof(int));
        table.Columns.Add("ProductId", typeof(int));
        table.Columns.Add("Quantity", typeof(int));
        table.Columns.Add("Tier", typeof(byte));
        table.Columns.Add("ExpectedPrice", typeof(decimal));
        foreach (var item in items)
        {
            ValidateMoney(item.ExpectedPrice);
            table.Rows.Add(table.Rows.Count + 1, item.ProductId, item.Quantity, TierNumber(item.Tier), item.ExpectedPrice);
        }
        AddTable(command, "@Items", "dbo.SaleItemSelection", table);
    }

    private static void AddTable(SqlCommand command, string name, string typeName, DataTable table)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Structured);
        parameter.TypeName = typeName;
        parameter.Value = table;
    }

    /// <summary>One saved return/exchange operation read back from SQL for the history tab.</summary>
    public sealed record ReturnExchangeRecord(
        int Id,
        string OperationNumber,
        int OperationType,
        DateTime CreatedAt,
        string ActorName,
        int InvoiceId,
        string InvoiceNumber,
        string CustomerName,
        string CustomerPhone,
        string Reason,
        string SettlementSummary,
        int GoodUnits,
        int DamagedUnits,
        int ReplacementUnits,
        decimal ReturnedValue,
        decimal ReplacementValue,
        string ReturnedItemsSummary,
        string ReplacementItemsSummary);

    /// <summary>Reads the saved return/exchange operations for the history tab.</summary>
    public List<ReturnExchangeRecord> LoadReturnExchanges()
    {
        var records = new List<ReturnExchangeRecord>();

        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT o.Id, o.OperationNumber, o.OperationType, o.CreatedAtUtc, u.DisplayName, o.SalesInvoiceId,
       si.InvoiceNumber, si.CustomerNameSnapshot, si.CustomerPhoneSnapshot, o.Reason, o.SettlementSummary,
       SUM(CASE WHEN l.LineKind = 1 AND l.UnitCondition = 0 THEN l.Quantity ELSE 0 END),
       SUM(CASE WHEN l.LineKind = 1 AND l.UnitCondition = 1 THEN l.Quantity ELSE 0 END),
       SUM(CASE WHEN l.LineKind = 2 THEN l.Quantity ELSE 0 END),
       SUM(CASE WHEN l.LineKind = 1 THEN COALESCE(l.NetAmount,l.Quantity*l.UnitPrice) ELSE 0 END),
       SUM(CASE WHEN l.LineKind = 2 THEN COALESCE(l.NetAmount,l.Quantity*l.UnitPrice) ELSE 0 END)
FROM dbo.ReturnExchanges o
JOIN dbo.Users u ON u.Id = o.ActorUserId
JOIN dbo.SalesInvoices si ON si.Id = o.SalesInvoiceId
LEFT JOIN dbo.ReturnExchangeLines l ON l.ReturnExchangeId = o.Id
GROUP BY o.Id, o.OperationNumber, o.OperationType, o.CreatedAtUtc, u.DisplayName, o.SalesInvoiceId,
         si.InvoiceNumber, si.CustomerNameSnapshot, si.CustomerPhoneSnapshot, o.Reason, o.SettlementSummary
ORDER BY o.CreatedAtUtc DESC, o.Id DESC;
SELECT ReturnExchangeId,LineKind,ProductNameSnapshot,Quantity,UnitCondition FROM dbo.ReturnExchangeLines ORDER BY ReturnExchangeId,Id;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                records.Add(new ReturnExchangeRecord(reader.GetInt32(0), reader.GetString(1), reader.GetByte(2),
                    AsLocal(reader.GetDateTime(3)), reader.GetString(4), reader.GetInt32(5), reader.GetString(6),
                    reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetString(10),
                    reader.GetInt32(11), reader.GetInt32(12), reader.GetInt32(13), reader.GetDecimal(14), reader.GetDecimal(15), "", ""));
            reader.NextResult();
            var summaries = new Dictionary<(int OperationId, int Kind), List<string>>();
            while (reader.Read())
            {
                var key = (reader.GetInt32(0), (int)reader.GetByte(1));
                if (!summaries.TryGetValue(key, out var parts)) summaries[key] = parts = [];
                var description = $"{reader.GetInt32(3)}× {reader.GetString(2)}";
                if (key.Item2 == 1) description += reader.GetByte(4) == 1 ? " (هالك)" : " (سليم)";
                parts.Add(description);
            }
            for (var index = 0; index < records.Count; index++)
            {
                var row = records[index];
                records[index] = row with
                {
                    ReturnedItemsSummary = string.Join("، ", summaries.GetValueOrDefault((row.Id, 1)) ?? []),
                    ReplacementItemsSummary = summaries.TryGetValue((row.Id, 2), out var parts) ? string.Join("، ", parts) : "—"
                };
            }
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            throw Describe(ex, "تعذر تحميل سجل المرتجع والاستبدال من قاعدة البيانات.");
        }

        return records;
    }

    private void Audit(
        SqlConnection connection, SqlTransaction transaction, string action, int entityId, string details, string entityType)
    {
        using var command = CreateCommand(connection, transaction, @"
INSERT dbo.AuditLog(ActorUserId,Module,ActionName,EntityType,EntityId,NewValuesJson)
VALUES(@Actor,N'Sales',@Action,@EntityType,@EntityId,JSON_MODIFY(N'{}',N'$.details',@Details));");
        command.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
        command.Parameters.Add("@Action", SqlDbType.NVarChar, 150).Value = action;
        command.Parameters.Add("@EntityType", SqlDbType.NVarChar, 100).Value = entityType;
        command.Parameters.Add("@EntityId", SqlDbType.NVarChar, 100).Value = entityId.ToString();
        command.Parameters.Add("@Details", SqlDbType.NVarChar, 1000).Value = Trim(details, 1000, "");
        command.ExecuteNonQuery();
    }

    private static SalesDataException Describe(Exception exception, string fallback)
    {
        if (exception is SalesDataException data) return data;
        if (exception is SqlException sql)
        {
            return sql.Number switch
            {
                2601 or 2627 => new SalesDataException("البيانات مكررة: رقم فاتورة أو هاتف عميل مسجل بالفعل. أعد المحاولة.", sql),
                547 => new SalesDataException("لا يمكن إتمام العملية لارتباط السجل بفاتورة أو حركة أخرى.", sql),
                1205 => new SalesDataException("تعارضت العملية مع مستخدم آخر. حدّث البيانات ثم أعد المحاولة.", sql),
                207 or 208 or 2715 or 2812 => new SalesDataException("تحتاج قاعدة البيانات إلى ترقية المبيعات. شغّل النسخة الجديدة من sql/07 ثم sql/22 على الخادم.", sql),
                229 or 230 => new SalesDataException(
                    "حساب الاتصال لا يملك صلاحية حفظ المبيعات. راجع منح حساب التشغيل وشغّل sql/22_EnableSaleSettlement.sql بعد سكربتات الصلاحيات.", sql),
                >= 50000 => new SalesDataException(sql.Message, sql),
                _ => new SalesDataException(fallback, sql)
            };
        }

        return new SalesDataException(fallback, exception);
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
            // Customer edits and the sale procedures touch filtered indexes, including on reused sessions.
            using var options = connection.CreateCommand();
            options.CommandText = "SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON; " +
                "SET ARITHABORT ON; SET CONCAT_NULL_YIELDS_NULL ON; SET NUMERIC_ROUNDABORT OFF;";
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
