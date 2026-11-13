using System.Collections.ObjectModel;
using System.Data;
using Microsoft.Data.SqlClient;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Data;

/// <summary>User-facing failure raised by the POS sales layer; the message is safe to show in the UI.</summary>
public sealed class SalesDataException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

public sealed record PosSaleLineRequest(
    int ProductId,
    string Barcode,
    string ProductName,
    string Category,
    string Unit,
    bool TrackSerialNumber,
    PriceTier PriceTier,
    int Quantity,
    decimal UnitPrice,
    decimal UnitCost);

public sealed record PosSaleRequest(
    int? CustomerId,
    string CustomerName,
    string CustomerPhone,
    string CustomerAddress,
    bool IsWalkIn,
    PriceTier PriceTier,
    decimal DiscountAmount,
    IReadOnlyList<PosSaleLineRequest> Lines,
    decimal CashPaid,
    decimal VodafonePaid,
    decimal InstaPayPaid,
    string Notes);

public sealed record SavedSale(int InvoiceId, string InvoiceNumber, decimal NetTotal, decimal PaidTotal, bool IsCredit);

/// <summary>SQL Server persistence for the POS sale, the customer directory and saved sales invoices.</summary>
public sealed class SalesRepository(ConnectionSettingsProvider settings, int actorUserId)
{
    private const string SalesSequence = "SalesInvoice";

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

    // ------------------------------------------------------------------- sales

    public SavedSale CompleteSale(PosSaleRequest request)
    {
        if (request.Lines.Count == 0) throw new SalesDataException("أضف صنفًا واحدًا على الأقل قبل إتمام الفاتورة.");
        foreach (var line in request.Lines)
        {
            if (line.Quantity <= 0) throw new SalesDataException($"كمية «{line.ProductName}» يجب أن تكون أكبر من صفر.");
            if (line.UnitPrice < 0m) throw new SalesDataException($"سعر «{line.ProductName}» غير صحيح.");
        }

        var subtotal = request.Lines.Sum(l => l.UnitPrice * l.Quantity);
        var discount = Math.Min(subtotal, Math.Max(0m, request.DiscountAmount));
        var netTotal = Math.Max(0m, subtotal - discount);
        var paidTotal = Math.Max(0m, request.CashPaid) + Math.Max(0m, request.VodafonePaid) + Math.Max(0m, request.InstaPayPaid);

        if (netTotal <= 0m) throw new SalesDataException("إجمالي الفاتورة صفر؛ أضف أصنافًا بأسعار صحيحة قبل الحفظ.");
        if (paidTotal <= 0m) throw new SalesDataException("أدخل مبلغًا مدفوعًا واحدًا على الأقل قبل إتمام الفاتورة.");
        if (request.IsWalkIn && paidTotal < netTotal)
            throw new SalesDataException("العميل النقدي (العام) يتطلب سداد كامل الفاتورة. اختر عميلًا مسجلًا للبيع الآجل.");
        if (!request.IsWalkIn && request.CustomerId is null)
            throw new SalesDataException("الفاتورة الآجلة تحتاج عميلًا مسجلًا محفوظًا في قاعدة البيانات.");
        if (paidTotal < netTotal && request.CustomerId is null)
            throw new SalesDataException("اختر عميلًا مسجلًا لبيع الفاتورة بجزء من قيمتها.");

        var isCredit = paidTotal < netTotal;

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var accounts = EnsureAccounts(connection, transaction);
                var invoiceNumber = NextDocumentNumber(connection, transaction, SalesSequence, DateTime.Now.Year, "INV");

                int invoiceId;
                using (var command = CreateCommand(connection, transaction, @"
INSERT dbo.SalesInvoices(InvoiceNumber,CustomerId,CustomerNameSnapshot,CustomerPhoneSnapshot,CustomerAddressSnapshot,
 IsWalkInCustomer,PriceTier,IssuedAtUtc,InvoiceStatus,DiscountAmount,Notes,CashierUserId)
OUTPUT inserted.Id
VALUES(@Number,@CustomerId,@Name,@Phone,@Address,@WalkIn,@Tier,SYSUTCDATETIME(),@Status,@Discount,@Notes,@Actor);"))
                {
                    command.Parameters.Add("@Number", SqlDbType.NVarChar, 50).Value = invoiceNumber;
                    command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = (object?)request.CustomerId ?? DBNull.Value;
                    command.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value = Trim(request.CustomerName, 200, "عميل نقدي (عام)");
                    command.Parameters.Add("@Phone", SqlDbType.NVarChar, 30).Value = Trim(request.CustomerPhone, 30, "");
                    command.Parameters.Add("@Address", SqlDbType.NVarChar, 500).Value = Trim(request.CustomerAddress, 500, "");
                    command.Parameters.Add("@WalkIn", SqlDbType.Bit).Value = request.IsWalkIn;
                    command.Parameters.Add("@Tier", SqlDbType.TinyInt).Value = (byte)request.PriceTier;
                    command.Parameters.Add("@Status", SqlDbType.TinyInt).Value = (byte)(isCredit ? SalesInvoiceStatus.PartialCredit : SalesInvoiceStatus.Completed);
                    command.Parameters.Add("@Discount", SqlDbType.Decimal).Value = discount;
                    command.Parameters["@Discount"].Precision = 19;
                    command.Parameters["@Discount"].Scale = 2;
                    command.Parameters.Add("@Notes", SqlDbType.NVarChar, 1000).Value = Trim(request.Notes, 1000, "");
                    command.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
                    invoiceId = (int)command.ExecuteScalar()!;
                }

                var lineNumber = 1;
                foreach (var line in request.Lines)
                {
                    var units = ClaimAvailableUnits(connection, transaction, line);
                    var unitCost = units.Count > 0
                        ? Math.Round(units.Average(u => u.Cost), 2, MidpointRounding.AwayFromZero)
                        : line.UnitCost;

                    int lineId;
                    using (var command = CreateCommand(connection, transaction, @"
INSERT dbo.SalesInvoiceLines(SalesInvoiceId,LineNumber,ProductId,BarcodeSnapshot,ProductNameSnapshot,CategoryNameSnapshot,
 UnitNameSnapshot,TrackSerialNumber,PriceTier,Quantity,ReturnedQuantity,UnitPrice,UnitCost,IsExchangeReplacement)
OUTPUT inserted.Id
VALUES(@Invoice,@LineNumber,@Product,@Barcode,@Name,@Category,@Unit,@Track,@Tier,@Quantity,0,@Price,@Cost,0);"))
                    {
                        command.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
                        command.Parameters.Add("@LineNumber", SqlDbType.Int).Value = lineNumber;
                        command.Parameters.Add("@Product", SqlDbType.Int).Value = line.ProductId;
                        command.Parameters.Add("@Barcode", SqlDbType.NVarChar, 100).Value = Trim(line.Barcode, 100, "");
                        command.Parameters.Add("@Name", SqlDbType.NVarChar, 250).Value = Trim(line.ProductName, 250, "");
                        command.Parameters.Add("@Category", SqlDbType.NVarChar, 200).Value = Trim(line.Category, 200, "");
                        command.Parameters.Add("@Unit", SqlDbType.NVarChar, 50).Value = Trim(line.Unit, 50, "قطعة");
                        command.Parameters.Add("@Track", SqlDbType.Bit).Value = line.TrackSerialNumber;
                        command.Parameters.Add("@Tier", SqlDbType.TinyInt).Value = (byte)line.PriceTier;
                        command.Parameters.Add("@Quantity", SqlDbType.Int).Value = line.Quantity;
                        command.Parameters.Add("@Price", SqlDbType.Decimal).Value = line.UnitPrice;
                        command.Parameters["@Price"].Precision = 19;
                        command.Parameters["@Price"].Scale = 2;
                        command.Parameters.Add("@Cost", SqlDbType.Decimal).Value = unitCost;
                        command.Parameters["@Cost"].Precision = 19;
                        command.Parameters["@Cost"].Scale = 2;
                        lineId = (int)command.ExecuteScalar()!;
                    }

                    foreach (var unit in units)
                        AllocateUnit(connection, transaction, invoiceId, lineId, invoiceNumber, line, unit);

                    lineNumber++;
                }

                var remainingToApply = netTotal;
                var cashApplied = ApplyPayment(connection, transaction, invoiceId, invoiceNumber, accounts,
                    FinancialAccountKind.Cash, request.CashPaid, ref remainingToApply, request.CustomerName);
                var vodafoneApplied = ApplyPayment(connection, transaction, invoiceId, invoiceNumber, accounts,
                    FinancialAccountKind.VodafoneCash, request.VodafonePaid, ref remainingToApply, request.CustomerName);
                var instaApplied = ApplyPayment(connection, transaction, invoiceId, invoiceNumber, accounts,
                    FinancialAccountKind.InstaPay, request.InstaPayPaid, ref remainingToApply, request.CustomerName);

                var paidSummary = BuildPaymentSummary(cashApplied, vodafoneApplied, instaApplied);
                AddActivity(connection, transaction, invoiceId, "إصدار فاتورة من نقطة البيع",
                    isCredit
                        ? $"سداد: {paidSummary} — متبقٍ {netTotal - paidTotal:0.00} ج.م على العميل."
                        : $"سداد: {paidSummary} ({netTotal:0.00} ج.م).",
                    isCredit ? paidTotal : netTotal);

                Audit(connection, transaction, "CreateSale", invoiceId,
                    $"فاتورة بيع {invoiceNumber} بإجمالي {netTotal:0.00} ومدفوع {paidTotal:0.00}", "SalesInvoice");

                transaction.Commit();
                return new SavedSale(invoiceId, invoiceNumber, netTotal, paidTotal, isCredit);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not SalesDataException))
        {
            throw Describe(ex, "تعذر حفظ الفاتورة في قاعدة البيانات.");
        }
    }

    public void CollectPayment(
        int invoiceId, decimal cash, decimal vodafone, decimal instaPay, decimal remainingBefore, string customerName, string note)
    {
        var requested = Math.Max(0m, cash) + Math.Max(0m, vodafone) + Math.Max(0m, instaPay);
        if (requested <= 0m) throw new SalesDataException("أدخل مبلغ تحصيل واحدًا على الأقل.");
        if (remainingBefore <= 0m) throw new SalesDataException("لا يوجد مبلغ متبقٍ على هذه الفاتورة.");
        if (requested > remainingBefore + 0.005m)
            throw new SalesDataException($"المبلغ المطلوب ({requested:0.00}) أكبر من المتبقي ({remainingBefore:0.00}).");
        if (note.Trim().Length > 1000) throw new SalesDataException("الملاحظات يجب ألا تتجاوز 1000 حرف.");

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var accounts = EnsureAccounts(connection, transaction);
                var remaining = remainingBefore;
                var cashApplied = ApplyPayment(connection, transaction, invoiceId, "", accounts,
                    FinancialAccountKind.Cash, cash, ref remaining, customerName);
                var vodafoneApplied = ApplyPayment(connection, transaction, invoiceId, "", accounts,
                    FinancialAccountKind.VodafoneCash, vodafone, ref remaining, customerName);
                var instaApplied = ApplyPayment(connection, transaction, invoiceId, "", accounts,
                    FinancialAccountKind.InstaPay, instaPay, ref remaining, customerName);

                var collected = cashApplied + vodafoneApplied + instaApplied;
                if (collected <= 0m) throw new SalesDataException("لم يتم تسجيل أي مبلغ على الفاتورة.");

                using (var command = CreateCommand(connection, transaction, @"
UPDATE dbo.SalesInvoices SET InvoiceStatus=CASE WHEN @Remaining <= 0 THEN 0 ELSE InvoiceStatus END,
 UpdatedAtUtc=SYSUTCDATETIME()
WHERE Id=@Id;"))
                {
                    command.Parameters.Add("@Remaining", SqlDbType.Decimal).Value = remaining;
                    command.Parameters["@Remaining"].Precision = 19;
                    command.Parameters["@Remaining"].Scale = 2;
                    command.Parameters.Add("@Id", SqlDbType.Int).Value = invoiceId;
                    command.ExecuteNonQuery();
                }

                var details = $"{BuildPaymentSummary(cashApplied, vodafoneApplied, instaApplied)} | المتبقي بعد التحصيل: {Math.Max(0m, remaining):0.00}";
                if (!string.IsNullOrWhiteSpace(note)) details += $" — {note.Trim()}";
                AddActivity(connection, transaction, invoiceId, "تحصيل دفعة متبقية", details, collected);
                Audit(connection, transaction, "CollectSalePayment", invoiceId,
                    $"تحصيل {collected:0.00} على الفاتورة #{invoiceId}", "SalesInvoice");

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not SalesDataException))
        {
            throw Describe(ex, "تعذر حفظ التحصيل في قاعدة البيانات.");
        }
    }

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
       l.ReturnedQuantity, l.UnitPrice, l.UnitCost, l.IsExchangeReplacement
FROM dbo.SalesInvoiceLines l
ORDER BY l.SalesInvoiceId, l.LineNumber;
SELECT lu.SalesInvoiceLineId, lu.InventoryUnitId, COALESCE(iu.SerialNumber,N''), lu.AllocationStatus
FROM dbo.SalesInvoiceLineUnits lu
JOIN dbo.InventoryUnits iu ON iu.Id = lu.InventoryUnitId;
SELECT p.SalesInvoiceId, a.AccountKind, p.PaymentKind, p.Amount
FROM dbo.SalesPayments p
JOIN dbo.FinancialAccounts a ON a.Id = p.FinancialAccountId;
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
                    IsExchangeReplacement = reader.GetBoolean(14)
                };

                invoice.Items.Add(line);
                linesById[line.DatabaseLineId] = line;

                var returned = reader.GetInt32(11);
                if (returned > 0) line.ReturnedQuantity = returned;
            }

            var cash = new Dictionary<int, decimal>();
            var vodafone = new Dictionary<int, decimal>();
            var insta = new Dictionary<int, decimal>();
            var refunded = new Dictionary<int, decimal>();

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
                if (!byId.ContainsKey(invoiceId)) continue;

                var kind = reader.GetByte(1);
                var paymentKind = reader.GetByte(2);
                var amount = reader.GetDecimal(3);

                if (paymentKind == 2)
                {
                    refunded[invoiceId] = refunded.GetValueOrDefault(invoiceId) + amount;
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

                invoice.ActivityLog.Add(new SalesInvoiceActivityEntry
                {
                    Timestamp = AsLocal(reader.GetDateTime(1)),
                    ActionTitle = reader.GetString(2),
                    Details = reader.GetString(3),
                    ActorName = reader.GetString(4),
                    Amount = reader.GetDecimal(5)
                });
            }

            foreach (var invoice in headers)
            {
                invoice.CashPaid = cash.GetValueOrDefault(invoice.Id);
                invoice.VodafoneCashPaid = vodafone.GetValueOrDefault(invoice.Id);
                invoice.InstaPayPaid = insta.GetValueOrDefault(invoice.Id);
                invoice.RefundedAmount = refunded.GetValueOrDefault(invoice.Id);
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

    public void UpdateNotes(int invoiceId, string notes)
    {
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
UPDATE dbo.SalesInvoices SET Notes=@Notes, UpdatedAtUtc=SYSUTCDATETIME() WHERE Id=@Id;";
            command.Parameters.Add("@Notes", SqlDbType.NVarChar, 1000).Value = Trim(notes, 1000, "");
            command.Parameters.Add("@Id", SqlDbType.Int).Value = invoiceId;
            if (command.ExecuteNonQuery() != 1)
                throw new SalesDataException("الفاتورة غير موجودة في قاعدة البيانات؛ حدّث شاشة الفواتير.");

            using var activity = new SqlCommand(@"
INSERT dbo.SalesInvoiceActivities(SalesInvoiceId,OccurredAtUtc,ActionTitle,Details,Amount,ActorUserId)
VALUES(@Invoice,SYSUTCDATETIME(),N'تحديث ملاحظات الفاتورة',@Details,0,@Actor);", connection);
            activity.CommandTimeout = 30;
            activity.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
            activity.Parameters.Add("@Details", SqlDbType.NVarChar, 1500).Value = Trim(notes, 1500, "تم مسح الملاحظات.");
            activity.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
            activity.ExecuteNonQuery();
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not SalesDataException))
        {
            throw Describe(ex, "تعذر حفظ ملاحظات الفاتورة في قاعدة البيانات.");
        }
    }

    /// <summary>Reverts a completed sale: returns every sold unit to stock, refunds the money and marks the invoice cancelled.</summary>
    public void CancelSale(SalesInvoiceDesignItem invoice)
    {
        if (invoice.Id <= 0) throw new SalesDataException("لا يمكن إلغاء فاتورة غير محفوظة في قاعدة البيانات.");
        if (invoice.Status is not (SalesInvoiceStatus.Completed or SalesInvoiceStatus.PartialCredit))
            throw new SalesDataException($"لا يمكن إلغاء الفاتورة {invoice.InvoiceNumber} في حالتها الحالية.");

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var accounts = EnsureAccounts(connection, transaction);
                var collected = LoadCollectedAmounts(connection, transaction, invoice.Id);

                using (var command = CreateCommand(connection, transaction, @"
SELECT lu.InventoryUnitId, iu.ProductId, iu.PurchaseCost, COALESCE(iu.SerialNumber,N'')
FROM dbo.SalesInvoiceLineUnits lu
JOIN dbo.SalesInvoiceLines l ON l.Id = lu.SalesInvoiceLineId
JOIN dbo.InventoryUnits iu ON iu.Id = lu.InventoryUnitId
WHERE l.SalesInvoiceId = @Invoice AND lu.AllocationStatus = 0 AND iu.UnitStatus = 1;"))
                {
                    command.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoice.Id;
                    using var reader = command.ExecuteReader();
                    var units = new List<(int UnitId, int ProductId, decimal Cost)>();
                    while (reader.Read())
                        units.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2)));
                    reader.Close();

                    foreach (var unit in units)
                        ReturnUnitToStock(connection, transaction, invoice.Id, invoice.InvoiceNumber,
                            unit.UnitId, unit.ProductId, unit.Cost, "Cancel", "إلغاء فاتورة بيع");
                }

                using (var command = CreateCommand(connection, transaction, @"
UPDATE dbo.SalesInvoiceLines SET ReturnedQuantity = Quantity, IsExchangeReplacement = 0
WHERE SalesInvoiceId = @Invoice;"))
                {
                    command.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoice.Id;
                    command.ExecuteNonQuery();
                }

                var refundTotal = collected.Cash + collected.Vodafone + collected.InstaPay;
                RefundAccounts(connection, transaction, invoice.Id, invoice.InvoiceNumber, accounts, collected, invoice.CustomerName);

                using (var command = CreateCommand(connection, transaction, @"
UPDATE dbo.SalesInvoices SET InvoiceStatus = 4, UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = @Id;"))
                {
                    command.Parameters.Add("@Id", SqlDbType.Int).Value = invoice.Id;
                    if (command.ExecuteNonQuery() != 1) throw new SalesDataException("الفاتورة غير موجودة في قاعدة البيانات.");
                }

                AddActivity(connection, transaction, invoice.Id, "إلغاء الفاتورة بالكامل",
                    $"تم إلغاء الفاتورة وإعادة القطع للمخزون ورد {refundTotal:0.00} ج.م.", refundTotal);
                Audit(connection, transaction, "CancelSale", invoice.Id,
                    $"إلغاء فاتورة {invoice.InvoiceNumber} بقيمة {invoice.NetTotal:0.00}", "SalesInvoice");

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not SalesDataException))
        {
            throw Describe(ex, "تعذر إلغاء الفاتورة في قاعدة البيانات.");
        }
    }

    public sealed record ReturnLineRequest(int SalesInvoiceLineId, int Quantity, bool ReturnAsGood, decimal UnitPrice, decimal UnitCost);

    public sealed record ReturnRequest(
        SalesInvoiceDesignItem Invoice,
        string Reason,
        bool RefundCashToCustomer,
        IReadOnlyList<ReturnLineRequest> Lines);

    /// <summary>Posts a customer return, moves the units back (good) or to damage, and records the refund when requested.</summary>
    public string CreateReturn(ReturnRequest request)
    {
        if (request.Invoice.Id <= 0) throw new SalesDataException("المرتجع يحتاج فاتورة محفوظة في قاعدة البيانات.");
        if (request.Lines.Count == 0) throw new SalesDataException("حدد كمية مرتجع واحدة على الأقل.");
        if (request.Lines.Any(l => l.Quantity <= 0)) throw new SalesDataException("كمية المرتجع يجب أن تكون أكبر من صفر.");
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new SalesDataException("يرجى تحديد سبب المرتجع.");

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var operationNumber = NextDocumentNumber(connection, transaction, "ReturnExchange", DateTime.Now.Year, "RET");
                var returnedValue = request.Lines.Sum(l => l.Quantity * l.UnitPrice);

                int operationId;
                using (var command = CreateCommand(connection, transaction, @"
INSERT dbo.ReturnExchanges(OperationNumber,SalesInvoiceId,OperationType,CreatedAtUtc,ActorUserId,Reason,SettlementSummary)
OUTPUT inserted.Id
VALUES(@Number,@Invoice,0,SYSUTCDATETIME(),@Actor,@Reason,@Settlement);"))
                {
                    command.Parameters.Add("@Number", SqlDbType.NVarChar, 50).Value = operationNumber;
                    command.Parameters.Add("@Invoice", SqlDbType.Int).Value = request.Invoice.Id;
                    command.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
                    command.Parameters.Add("@Reason", SqlDbType.NVarChar, 1000).Value = Trim(request.Reason, 1000, "");
                    command.Parameters.Add("@Settlement", SqlDbType.NVarChar, 1500).Value =
                        request.RefundCashToCustomer
                            ? $"رد للعميل: {returnedValue:0.00} ج.م"
                            : $"خصم {returnedValue:0.00} ج.م من رصيد العميل";
                    operationId = (int)command.ExecuteScalar()!;
                }

                foreach (var line in request.Lines)
                {
                    var snapshot = LoadLineSnapshot(connection, transaction, line.SalesInvoiceLineId, request.Invoice.Id);

                    if (line.Quantity > snapshot.RemainingQuantity)
                        throw new SalesDataException(
                            $"الكمية المتاحة للمرتجع من «{snapshot.ProductName}» هي {snapshot.RemainingQuantity} فقط.");

                    int returnLineId;
                    using (var command = CreateCommand(connection, transaction, @"
INSERT dbo.ReturnExchangeLines(ReturnExchangeId,LineKind,SalesInvoiceLineId,ProductId,ProductNameSnapshot,Quantity,UnitPrice,UnitCost,UnitCondition)
OUTPUT inserted.Id
VALUES(@Operation,1,@SalesLine,@Product,@Name,@Quantity,@Price,@Cost,@Condition);"))
                    {
                        command.Parameters.Add("@Operation", SqlDbType.Int).Value = operationId;
                        command.Parameters.Add("@SalesLine", SqlDbType.Int).Value = line.SalesInvoiceLineId;
                        command.Parameters.Add("@Product", SqlDbType.Int).Value = snapshot.ProductId;
                        command.Parameters.Add("@Name", SqlDbType.NVarChar, 250).Value = snapshot.ProductName;
                        command.Parameters.Add("@Quantity", SqlDbType.Int).Value = line.Quantity;
                        command.Parameters.Add("@Price", SqlDbType.Decimal).Value = line.UnitPrice;
                        command.Parameters["@Price"].Precision = 19;
                        command.Parameters["@Price"].Scale = 2;
                        command.Parameters.Add("@Cost", SqlDbType.Decimal).Value = line.UnitCost;
                        command.Parameters["@Cost"].Precision = 19;
                        command.Parameters["@Cost"].Scale = 2;
                        command.Parameters.Add("@Condition", SqlDbType.TinyInt).Value = (byte)(line.ReturnAsGood ? 0 : 1);
                        returnLineId = (int)command.ExecuteScalar()!;
                    }

                    var units = ClaimSoldUnits(connection, transaction, line.SalesInvoiceLineId, line.Quantity);
                    foreach (var unit in units)
                    {
                        using (var link = CreateCommand(connection, transaction,
                            "INSERT dbo.ReturnExchangeUnits(ReturnExchangeLineId,InventoryUnitId) VALUES(@Line,@Unit);"))
                        {
                            link.Parameters.Add("@Line", SqlDbType.Int).Value = returnLineId;
                            link.Parameters.Add("@Unit", SqlDbType.Int).Value = unit.UnitId;
                            link.ExecuteNonQuery();
                        }

                        using (var mark = CreateCommand(connection, transaction,
                            "UPDATE dbo.SalesInvoiceLineUnits SET AllocationStatus = 1 WHERE SalesInvoiceLineId=@Line AND InventoryUnitId=@Unit;"))
                        {
                            mark.Parameters.Add("@Line", SqlDbType.Int).Value = line.SalesInvoiceLineId;
                            mark.Parameters.Add("@Unit", SqlDbType.Int).Value = unit.UnitId;
                            mark.ExecuteNonQuery();
                        }

                        if (line.ReturnAsGood)
                            ReturnUnitToStock(connection, transaction, request.Invoice.Id, request.Invoice.InvoiceNumber,
                                unit.UnitId, snapshot.ProductId, unit.Cost, "Return", $"مرتجع {request.Reason}");
                        else
                            MoveUnitToDamaged(connection, transaction, request.Invoice.Id, unit.UnitId,
                                snapshot.ProductId, unit.Cost, $"مرتجع فاتورة {request.Invoice.InvoiceNumber}: {request.Reason}");
                    }

                    using var updateLine = CreateCommand(connection, transaction,
                        "UPDATE dbo.SalesInvoiceLines SET ReturnedQuantity = ReturnedQuantity + @Quantity WHERE Id=@Id;");
                    updateLine.Parameters.Add("@Quantity", SqlDbType.Int).Value = line.Quantity;
                    updateLine.Parameters.Add("@Id", SqlDbType.Int).Value = line.SalesInvoiceLineId;
                    updateLine.ExecuteNonQuery();
                }

                using (var status = CreateCommand(connection, transaction, @"
UPDATE dbo.SalesInvoices
SET InvoiceStatus = CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.SalesInvoiceLines WHERE SalesInvoiceId=@Id AND ReturnedQuantity < Quantity)
                         THEN 3 ELSE 2 END,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE Id = @Id;"))
                {
                    status.Parameters.Add("@Id", SqlDbType.Int).Value = request.Invoice.Id;
                    status.ExecuteNonQuery();
                }

                if (request.RefundCashToCustomer && returnedValue > 0m)
                {
                    var cashAccount = EnsureAccounts(connection, transaction)[FinancialAccountKind.Cash];
                    using var refund = CreateCommand(connection, transaction, @"
INSERT dbo.SalesPayments(SalesInvoiceId,FinancialAccountId,PaymentKind,Amount,PaidAtUtc,Note,ActorUserId)
VALUES(@Invoice,@Account,2,@Amount,SYSUTCDATETIME(),N'رد نقدي لمرتجع',@Actor);");
                    refund.Parameters.Add("@Invoice", SqlDbType.Int).Value = request.Invoice.Id;
                    refund.Parameters.Add("@Account", SqlDbType.Int).Value = cashAccount;
                    refund.Parameters.Add("@Amount", SqlDbType.Decimal).Value = returnedValue;
                    refund.Parameters["@Amount"].Precision = 19;
                    refund.Parameters["@Amount"].Scale = 2;
                    refund.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
                    refund.ExecuteNonQuery();

                    decimal balanceAfter;
                    using (var balance = CreateCommand(connection, transaction, @"
UPDATE dbo.FinancialAccounts SET CurrentBalance = CurrentBalance - @Amount, UpdatedAtUtc = SYSUTCDATETIME()
OUTPUT inserted.CurrentBalance WHERE Id = @Id;"))
                    {
                        balance.Parameters.Add("@Amount", SqlDbType.Decimal).Value = returnedValue;
                        balance.Parameters["@Amount"].Precision = 19;
                        balance.Parameters["@Amount"].Scale = 2;
                        balance.Parameters.Add("@Id", SqlDbType.Int).Value = cashAccount;
                        balanceAfter = (decimal)balance.ExecuteScalar()!;
                    }

                    using var ledger = CreateCommand(connection, transaction, @"
INSERT dbo.FinancialTransactions(FinancialAccountId,OccurredAtUtc,IsIncoming,Category,Amount,BalanceAfter,Counterparty,Note,
 ActorUserId,CountsTowardLimits,SourceEntityType,SourceEntityId)
VALUES(@Account,SYSUTCDATETIME(),0,N'رد مرتجع مبيعات',@Amount,@Balance,@Counterparty,@Note,@Actor,0,N'ReturnExchange',@Operation);");
                    ledger.Parameters.Add("@Account", SqlDbType.Int).Value = cashAccount;
                    ledger.Parameters.Add("@Amount", SqlDbType.Decimal).Value = returnedValue;
                    ledger.Parameters["@Amount"].Precision = 19;
                    ledger.Parameters["@Amount"].Scale = 2;
                    ledger.Parameters.Add("@Balance", SqlDbType.Decimal).Value = balanceAfter;
                    ledger.Parameters["@Balance"].Precision = 19;
                    ledger.Parameters["@Balance"].Scale = 2;
                    ledger.Parameters.Add("@Counterparty", SqlDbType.NVarChar, 250).Value =
                        Trim(request.Invoice.CustomerName, 250, "عميل نقدي");
                    ledger.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value = $"مرتجع {operationNumber}";
                    ledger.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
                    ledger.Parameters.Add("@Operation", SqlDbType.Int).Value = operationId;
                    ledger.ExecuteNonQuery();
                }

                AddActivity(connection, transaction, request.Invoice.Id,
                    request.Lines.Sum(l => l.Quantity) == 0 ? "مرتجع" : "مرتجع جزئي",
                    $"مرتجع بقيمة {returnedValue:0.00} ج.م ({(request.RefundCashToCustomer ? "رد نقدي" : "خصم من الرصيد")}) — {request.Reason}.",
                    returnedValue);
                Audit(connection, transaction, "CreateReturn", request.Invoice.Id,
                    $"مرتجع {operationNumber} على الفاتورة {request.Invoice.InvoiceNumber}", "SalesInvoice");

                transaction.Commit();
                return operationNumber;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not SalesDataException))
        {
            throw Describe(ex, "تعذر حفظ المرتجع في قاعدة البيانات.");
        }
    }

    private sealed record LineSnapshot(int ProductId, string ProductName, int RemainingQuantity);

    private static LineSnapshot LoadLineSnapshot(
        SqlConnection connection, SqlTransaction transaction, int salesLineId, int invoiceId)
    {
        using var command = CreateCommand(connection, transaction, @"
SELECT ProductId, ProductNameSnapshot, Quantity - ReturnedQuantity
FROM dbo.SalesInvoiceLines WHERE Id=@Id AND SalesInvoiceId=@Invoice;");
        command.Parameters.Add("@Id", SqlDbType.Int).Value = salesLineId;
        command.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new SalesDataException("بند الفاتورة غير موجود في قاعدة البيانات؛ حدّث شاشة الفواتير.");
        return new LineSnapshot(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2));
    }

    private sealed record SoldUnit(int UnitId, decimal Cost);

    private static List<SoldUnit> ClaimSoldUnits(
        SqlConnection connection, SqlTransaction transaction, int salesLineId, int quantity)
    {
        var units = new List<SoldUnit>();
        using (var command = CreateCommand(connection, transaction, @"
SELECT TOP (@Quantity) lu.InventoryUnitId, iu.PurchaseCost
FROM dbo.SalesInvoiceLineUnits lu WITH (UPDLOCK, HOLDLOCK)
JOIN dbo.InventoryUnits iu ON iu.Id = lu.InventoryUnitId
WHERE lu.SalesInvoiceLineId = @Line AND lu.AllocationStatus = 0 AND iu.UnitStatus = 1
ORDER BY lu.InventoryUnitId;"))
        {
            command.Parameters.Add("@Quantity", SqlDbType.Int).Value = quantity;
            command.Parameters.Add("@Line", SqlDbType.Int).Value = salesLineId;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                units.Add(new SoldUnit(reader.GetInt32(0), reader.GetDecimal(1)));
        }

        if (units.Count < quantity)
            throw new SalesDataException(
                $"لا توجد وحدات كافية مرتبطة بهذا البند ({units.Count} متاح من {quantity}). حدّث شاشة الفواتير وأعد المحاولة.");

        return units;
    }

    private void ReturnUnitToStock(
        SqlConnection connection, SqlTransaction transaction, int invoiceId, string invoiceNumber,
        int unitId, int productId, decimal cost, string movementType, string reason)
    {
        using (var update = CreateCommand(connection, transaction, @"
UPDATE dbo.InventoryUnits SET UnitStatus = 0, SaleInvoiceNumber = N'', SoldAtUtc = NULL, DamagedAtUtc = NULL,
 DamageReason = N'', DamageSettlementStatus = N''
WHERE Id = @Id;"))
        {
            update.Parameters.Add("@Id", SqlDbType.Int).Value = unitId;
            update.ExecuteNonQuery();
        }

        using var movement = CreateCommand(connection, transaction, @"
INSERT dbo.StockMovements(ProductId,InventoryUnitId,MovementType,QuantityDelta,UnitCost,ActorUserId,SourceEntityType,SourceEntityId,Reason)
VALUES(@Product,@Unit,@Type,1,@Cost,@Actor,N'SalesInvoice',@Invoice,@Reason);");
        movement.Parameters.Add("@Product", SqlDbType.Int).Value = productId;
        movement.Parameters.Add("@Unit", SqlDbType.Int).Value = unitId;
        movement.Parameters.Add("@Type", SqlDbType.NVarChar, 40).Value = movementType;
        movement.Parameters.Add("@Cost", SqlDbType.Decimal).Value = cost;
        movement.Parameters["@Cost"].Precision = 19;
        movement.Parameters["@Cost"].Scale = 2;
        movement.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
        movement.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
        movement.Parameters.Add("@Reason", SqlDbType.NVarChar, 1000).Value = Trim(reason, 1000, "");
        movement.ExecuteNonQuery();
        _ = invoiceNumber;
    }

    private static void MoveUnitToDamaged(
        SqlConnection connection, SqlTransaction transaction, int invoiceId,
        int unitId, int productId, decimal cost, string reason)
    {
        using (var update = CreateCommand(connection, transaction, @"
UPDATE dbo.InventoryUnits SET UnitStatus = 2, SaleInvoiceNumber = N'', SoldAtUtc = NULL,
 DamagedAtUtc = SYSUTCDATETIME(), DamageReason = @Reason, DamageSettlementStatus = N'قيد المراجعة'
WHERE Id = @Id;"))
        {
            update.Parameters.Add("@Reason", SqlDbType.NVarChar, 1000).Value = Trim(reason, 1000, "");
            update.Parameters.Add("@Id", SqlDbType.Int).Value = unitId;
            update.ExecuteNonQuery();
        }

        using var movement = CreateCommand(connection, transaction, @"
INSERT dbo.StockMovements(ProductId,InventoryUnitId,MovementType,QuantityDelta,UnitCost,ActorUserId,SourceEntityType,SourceEntityId,Reason)
VALUES(@Product,@Unit,N'Damage',-1,@Cost,@Actor,N'SalesInvoice',@Invoice,@Reason);");
        movement.Parameters.Add("@Product", SqlDbType.Int).Value = productId;
        movement.Parameters.Add("@Unit", SqlDbType.Int).Value = unitId;
        movement.Parameters.Add("@Cost", SqlDbType.Decimal).Value = cost;
        movement.Parameters["@Cost"].Precision = 19;
        movement.Parameters["@Cost"].Scale = 2;
        movement.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
        movement.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
        movement.Parameters.Add("@Reason", SqlDbType.NVarChar, 1000).Value = Trim(reason, 1000, "");
        movement.ExecuteNonQuery();
    }

    private sealed record CollectedAmounts(decimal Cash, decimal Vodafone, decimal InstaPay);

    private static CollectedAmounts LoadCollectedAmounts(SqlConnection connection, SqlTransaction transaction, int invoiceId)
    {
        var cash = 0m;
        var vodafone = 0m;
        var insta = 0m;

        using var command = CreateCommand(connection, transaction, @"
SELECT a.AccountKind, SUM(p.Amount)
FROM dbo.SalesPayments p
JOIN dbo.FinancialAccounts a ON a.Id = p.FinancialAccountId
WHERE p.SalesInvoiceId = @Invoice AND p.PaymentKind = 1
GROUP BY a.AccountKind;");
        command.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var total = reader.GetDecimal(1);
            switch (reader.GetByte(0))
            {
                case 0: cash += total; break;
                case 1: vodafone += total; break;
                default: insta += total; break;
            }
        }

        return new CollectedAmounts(cash, vodafone, insta);
    }

    private void RefundAccounts(
        SqlConnection connection, SqlTransaction transaction, int invoiceId, string invoiceNumber,
        IReadOnlyDictionary<FinancialAccountKind, int> accounts, CollectedAmounts collected, string customerName)
    {
        RefundOne(connection, transaction, invoiceId, invoiceNumber, accounts[FinancialAccountKind.Cash],
            collected.Cash, customerName);
        RefundOne(connection, transaction, invoiceId, invoiceNumber, accounts[FinancialAccountKind.VodafoneCash],
            collected.Vodafone, customerName);
        RefundOne(connection, transaction, invoiceId, invoiceNumber, accounts[FinancialAccountKind.InstaPay],
            collected.InstaPay, customerName);
    }

    private void RefundOne(
        SqlConnection connection, SqlTransaction transaction, int invoiceId, string invoiceNumber,
        int accountId, decimal amount, string customerName)
    {
        if (amount <= 0m) return;

        using (var payment = CreateCommand(connection, transaction, @"
INSERT dbo.SalesPayments(SalesInvoiceId,FinancialAccountId,PaymentKind,Amount,PaidAtUtc,Note,ActorUserId)
VALUES(@Invoice,@Account,2,@Amount,SYSUTCDATETIME(),N'رد مبلغ فاتورة ملغاة',@Actor);"))
        {
            payment.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
            payment.Parameters.Add("@Account", SqlDbType.Int).Value = accountId;
            payment.Parameters.Add("@Amount", SqlDbType.Decimal).Value = amount;
            payment.Parameters["@Amount"].Precision = 19;
            payment.Parameters["@Amount"].Scale = 2;
            payment.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
            payment.ExecuteNonQuery();
        }

        decimal balanceAfter;
        using (var balance = CreateCommand(connection, transaction, @"
UPDATE dbo.FinancialAccounts SET CurrentBalance = CurrentBalance - @Amount, UpdatedAtUtc = SYSUTCDATETIME()
OUTPUT inserted.CurrentBalance WHERE Id = @Id;"))
        {
            balance.Parameters.Add("@Amount", SqlDbType.Decimal).Value = amount;
            balance.Parameters["@Amount"].Precision = 19;
            balance.Parameters["@Amount"].Scale = 2;
            balance.Parameters.Add("@Id", SqlDbType.Int).Value = accountId;
            balanceAfter = (decimal)balance.ExecuteScalar()!;
        }

        using var ledger = CreateCommand(connection, transaction, @"
INSERT dbo.FinancialTransactions(FinancialAccountId,OccurredAtUtc,IsIncoming,Category,Amount,BalanceAfter,Counterparty,Note,
 ActorUserId,CountsTowardLimits,SourceEntityType,SourceEntityId)
VALUES(@Account,SYSUTCDATETIME(),0,N'رد فاتورة ملغاة',@Amount,@Balance,@Counterparty,@Note,@Actor,0,N'SalesInvoice',@Invoice);");
        ledger.Parameters.Add("@Account", SqlDbType.Int).Value = accountId;
        ledger.Parameters.Add("@Amount", SqlDbType.Decimal).Value = amount;
        ledger.Parameters["@Amount"].Precision = 19;
        ledger.Parameters["@Amount"].Scale = 2;
        ledger.Parameters.Add("@Balance", SqlDbType.Decimal).Value = balanceAfter;
        ledger.Parameters["@Balance"].Precision = 19;
        ledger.Parameters["@Balance"].Scale = 2;
        ledger.Parameters.Add("@Counterparty", SqlDbType.NVarChar, 250).Value = Trim(customerName, 250, "عميل نقدي");
        ledger.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value = $"إلغاء {invoiceNumber}";
        ledger.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
        ledger.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
        ledger.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ helpers

    private sealed record ClaimedUnit(int Id, decimal Cost);

    private static List<ClaimedUnit> ClaimAvailableUnits(
        SqlConnection connection, SqlTransaction transaction, PosSaleLineRequest line)
    {
        var units = new List<ClaimedUnit>();
        using (var command = CreateCommand(connection, transaction, @"
SELECT TOP (@Quantity) iu.Id, iu.PurchaseCost
FROM dbo.InventoryUnits iu WITH (UPDLOCK, HOLDLOCK)
WHERE iu.ProductId = @ProductId AND iu.UnitStatus = 0
ORDER BY iu.RegisteredAtUtc, iu.Id;"))
        {
            command.Parameters.Add("@Quantity", SqlDbType.Int).Value = line.Quantity;
            command.Parameters.Add("@ProductId", SqlDbType.Int).Value = line.ProductId;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                units.Add(new ClaimedUnit(reader.GetInt32(0), reader.GetDecimal(1)));
        }

        if (units.Count < line.Quantity)
        {
            throw new SalesDataException(
                $"المتاح من «{line.ProductName}» هو {units.Count} قطعة فقط، والمطلوب {line.Quantity}. " +
                "حدّث الأصناف من قاعدة البيانات وعدّل الكمية.");
        }

        return units;
    }

    private void AllocateUnit(
        SqlConnection connection, SqlTransaction transaction, int invoiceId, int lineId,
        string invoiceNumber, PosSaleLineRequest line, ClaimedUnit unit)
    {
        using (var update = CreateCommand(connection, transaction, @"
UPDATE dbo.InventoryUnits
SET UnitStatus = 1, SaleInvoiceNumber = @InvoiceNumber, SoldAtUtc = SYSUTCDATETIME()
WHERE Id = @Id AND UnitStatus = 0;"))
        {
            update.Parameters.Add("@InvoiceNumber", SqlDbType.NVarChar, 100).Value = invoiceNumber;
            update.Parameters.Add("@Id", SqlDbType.Int).Value = unit.Id;
            if (update.ExecuteNonQuery() != 1)
                throw new SalesDataException($"تغيرت حالة وحدة من «{line.ProductName}» أثناء الحفظ. حدّث الأصناف وأعد المحاولة.");
        }

        using (var link = CreateCommand(connection, transaction,
            "INSERT dbo.SalesInvoiceLineUnits(SalesInvoiceLineId,InventoryUnitId,AllocationStatus) VALUES(@Line,@Unit,0);"))
        {
            link.Parameters.Add("@Line", SqlDbType.Int).Value = lineId;
            link.Parameters.Add("@Unit", SqlDbType.Int).Value = unit.Id;
            link.ExecuteNonQuery();
        }

        using var movement = CreateCommand(connection, transaction, @"
INSERT dbo.StockMovements(ProductId,InventoryUnitId,MovementType,QuantityDelta,UnitCost,ActorUserId,SourceEntityType,SourceEntityId,Reason)
VALUES(@Product,@Unit,N'Sale',-1,@Cost,@Actor,N'SalesInvoice',@Invoice,N'بيع من نقطة البيع');");
        movement.Parameters.Add("@Product", SqlDbType.Int).Value = line.ProductId;
        movement.Parameters.Add("@Unit", SqlDbType.Int).Value = unit.Id;
        movement.Parameters.Add("@Cost", SqlDbType.Decimal).Value = unit.Cost;
        movement.Parameters["@Cost"].Precision = 19;
        movement.Parameters["@Cost"].Scale = 2;
        movement.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
        movement.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
        movement.ExecuteNonQuery();
    }

    private decimal ApplyPayment(
        SqlConnection connection, SqlTransaction transaction, int invoiceId, string invoiceNumber,
        IReadOnlyDictionary<FinancialAccountKind, int> accounts, FinancialAccountKind kind,
        decimal amount, ref decimal remainingToApply, string customerName)
    {
        if (amount <= 0m || remainingToApply <= 0m) return 0m;

        var applied = Math.Min(amount, remainingToApply);
        applied = Math.Round(applied, 2, MidpointRounding.AwayFromZero);
        if (applied <= 0m) return 0m;
        remainingToApply -= applied;

        var accountId = accounts[kind];

        using (var payment = CreateCommand(connection, transaction, @"
INSERT dbo.SalesPayments(SalesInvoiceId,FinancialAccountId,PaymentKind,Amount,PaidAtUtc,Note,ActorUserId)
VALUES(@Invoice,@Account,1,@Amount,SYSUTCDATETIME(),N'تحصيل من نقطة البيع',@Actor);"))
        {
            payment.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
            payment.Parameters.Add("@Account", SqlDbType.Int).Value = accountId;
            payment.Parameters.Add("@Amount", SqlDbType.Decimal).Value = applied;
            payment.Parameters["@Amount"].Precision = 19;
            payment.Parameters["@Amount"].Scale = 2;
            payment.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
            payment.ExecuteNonQuery();
        }

        decimal balanceAfter;
        using (var balance = CreateCommand(connection, transaction, @"
UPDATE dbo.FinancialAccounts SET CurrentBalance = CurrentBalance + @Amount, UpdatedAtUtc = SYSUTCDATETIME()
OUTPUT inserted.CurrentBalance
WHERE Id = @Id;"))
        {
            balance.Parameters.Add("@Amount", SqlDbType.Decimal).Value = applied;
            balance.Parameters["@Amount"].Precision = 19;
            balance.Parameters["@Amount"].Scale = 2;
            balance.Parameters.Add("@Id", SqlDbType.Int).Value = accountId;
            balanceAfter = (decimal)balance.ExecuteScalar()!;
        }

        using var ledger = CreateCommand(connection, transaction, @"
INSERT dbo.FinancialTransactions(FinancialAccountId,OccurredAtUtc,IsIncoming,Category,Amount,BalanceAfter,Counterparty,Note,
 ActorUserId,CountsTowardLimits,SourceEntityType,SourceEntityId)
VALUES(@Account,SYSUTCDATETIME(),1,N'تحصيل مبيعات',@Amount,@Balance,@Counterparty,@Note,@Actor,@Limits,N'SalesInvoice',@Invoice);");
        ledger.Parameters.Add("@Account", SqlDbType.Int).Value = accountId;
        ledger.Parameters.Add("@Amount", SqlDbType.Decimal).Value = applied;
        ledger.Parameters["@Amount"].Precision = 19;
        ledger.Parameters["@Amount"].Scale = 2;
        ledger.Parameters.Add("@Balance", SqlDbType.Decimal).Value = balanceAfter;
        ledger.Parameters["@Balance"].Precision = 19;
        ledger.Parameters["@Balance"].Scale = 2;
        ledger.Parameters.Add("@Counterparty", SqlDbType.NVarChar, 250).Value = Trim(customerName, 250, "عميل نقدي");
        ledger.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value =
            string.IsNullOrWhiteSpace(invoiceNumber) ? "تحصيل دفعة متبقية" : $"فاتورة {invoiceNumber}";
        ledger.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
        ledger.Parameters.Add("@Limits", SqlDbType.Bit).Value = kind != FinancialAccountKind.Cash;
        ledger.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
        ledger.ExecuteNonQuery();

        return applied;
    }

    private void AddActivity(
        SqlConnection connection, SqlTransaction transaction, int invoiceId, string title, string details, decimal amount)
    {
        using var command = CreateCommand(connection, transaction, @"
INSERT dbo.SalesInvoiceActivities(SalesInvoiceId,OccurredAtUtc,ActionTitle,Details,Amount,ActorUserId)
VALUES(@Invoice,SYSUTCDATETIME(),@Title,@Details,@Amount,@Actor);");
        command.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
        command.Parameters.Add("@Title", SqlDbType.NVarChar, 200).Value = Trim(title, 200, "حركة فاتورة");
        command.Parameters.Add("@Details", SqlDbType.NVarChar, 1500).Value = Trim(details, 1500, "");
        command.Parameters.Add("@Amount", SqlDbType.Decimal).Value = amount;
        command.Parameters["@Amount"].Precision = 19;
        command.Parameters["@Amount"].Scale = 2;
        command.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
        command.ExecuteNonQuery();
    }

    /// <summary>Creates the single cash account and the default wallet accounts when the database has none yet.</summary>
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
VALUES(@Kind,@Alias,@Identifier,0,1,1,N'أنشئت تلقائيًا لتحصيل المبيعات');");
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
                229 or 230 => new SalesDataException(
                    "حساب الاتصال لا يملك صلاحية حفظ المبيعات. شغّل sql/09_EnablePosSales.sql على الخادم ثم أعد المحاولة.", sql),
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
        connection.Open();
        return connection;
    }

    private static SqlCommand CreateCommand(SqlConnection connection, SqlTransaction transaction, string sql) =>
        new(sql, connection, transaction) { CommandTimeout = 30 };

    private static DateTime AsLocal(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
}
