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

                    // Units are optional: a product tracked only by StockMovements still moves its balance.
                    var untrackedQuantity = line.Quantity - units.Count;
                    if (untrackedQuantity > 0)
                    {
                        RecordUntrackedSale(connection, transaction, invoiceId, invoiceNumber, line,
                            untrackedQuantity, unitCost);
                    }

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

    /// <summary>
    /// One returned line. A line can carry both conditions at once (good + damaged), which the
    /// exchange screen allows; the invoice dialog always sends one of them.
    /// </summary>
    public sealed record ReturnLineRequest(
        int SalesInvoiceLineId,
        int Quantity,
        bool ReturnAsGood,
        decimal UnitPrice,
        decimal UnitCost,
        int GoodQuantity = -1)
    {
        public int GoodQuantityValue => GoodQuantity < 0 ? (ReturnAsGood ? Quantity : 0) : Math.Clamp(GoodQuantity, 0, Quantity);
        public int DamagedQuantity => Quantity - GoodQuantityValue;
        public bool IsSplitCondition => GoodQuantityValue > 0 && DamagedQuantity > 0;
    }

    public sealed record ReturnRequest(
        SalesInvoiceDesignItem Invoice,
        string Reason,
        bool RefundCashToCustomer,
        IReadOnlyList<ReturnLineRequest> Lines);

    /// <summary>A replacement and the price tier the customer agreed on.</summary>
    public sealed record ExchangeReplacementRequest(int ProductId, int Quantity, int PriceTier);

    /// <summary>Extra money the customer pays for an upgrade exchange (split by payment method).</summary>
    public sealed record ExchangePayment(decimal Cash, decimal Vodafone, decimal InstaPay);

    public sealed record ExchangeRequest(
        SalesInvoiceDesignItem Invoice,
        string Reason,
        IReadOnlyList<ReturnLineRequest> Lines,
        IReadOnlyList<ExchangeReplacementRequest> Replacements);

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

    /// <summary>
    /// Posts a customer <b>exchange</b> in one serializable transaction: the returned units go back to
    /// stock (good) or to damage, the replacement units are claimed for the same invoice, the invoice
    /// price difference is settled (refund, extra payment, or credit), and everything is written to
    /// ReturnExchanges / ReturnExchangeLines / ReturnExchangeUnits / StockMovements / AuditLog.
    /// </summary>
    public string CreateExchange(ExchangeRequest request, ExchangePayment payment)
    {
        if (request.Invoice.Id <= 0) throw new SalesDataException("الاستبدال يحتاج فاتورة محفوظة في قاعدة البيانات.");
        if (request.Lines.Count == 0) throw new SalesDataException("حدد كمية مرتجعة واحدة على الأقل للاستبدال.");
        if (request.Replacements.Count == 0) throw new SalesDataException("اختر صنفًا بديلًا واحدًا على الأقل.");
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new SalesDataException("يرجى تحديد سبب الاستبدال.");
        if (payment.Cash < 0m || payment.Vodafone < 0m || payment.InstaPay < 0m)
            throw new SalesDataException("مبالغ فرق الاستبدال لا يمكن أن تكون سالبة.");

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var operationNumber = NextDocumentNumber(connection, transaction, "ReturnExchange", DateTime.Now.Year, "EXC");

                // Prices are resolved before the operation row is inserted so the settlement summary
                // is written once (no UPDATE grant needed on dbo.ReturnExchanges).
                var planned = new List<(ExchangeReplacementRequest Request, SaleProductSnapshot Product, decimal Price)>();
                foreach (var replacement in request.Replacements.Where(r => r.Quantity > 0))
                {
                    var snapshotProduct = LoadProductForSale(connection, transaction, replacement.ProductId);
                    var tierPrice = replacement.PriceTier switch
                    {
                        2 => snapshotProduct.WholesalePrice,
                        1 => snapshotProduct.SpecialPrice,
                        _ => snapshotProduct.RetailPrice
                    };
                    planned.Add((replacement, snapshotProduct, tierPrice));
                }

                var returnedValue = request.Lines.Where(l => l.Quantity > 0).Sum(l => l.Quantity * l.UnitPrice);
                var replacementValue = planned.Sum(p => p.Request.Quantity * p.Price);
                var difference = replacementValue - returnedValue;
                var paidTotal = payment.Cash + payment.Vodafone + payment.InstaPay;
                if (difference > 0m && paidTotal > difference + 0.009m)
                    throw new SalesDataException(
                        $"المبلغ المدفوع ({paidTotal:0.00}) أكبر من فرق الاستبدال المستحق ({difference:0.00}).");

                var settlementParts = new List<string>(3);
                if (payment.Cash > 0m) settlementParts.Add($"كاش {payment.Cash:0.00}");
                if (payment.Vodafone > 0m) settlementParts.Add($"فودافون {payment.Vodafone:0.00}");
                if (payment.InstaPay > 0m) settlementParts.Add($"إنستاباي {payment.InstaPay:0.00}");

                string settlement;
                if (difference > 0m)
                {
                    var creditGap = Math.Max(0m, difference - paidTotal);
                    settlement = creditGap > 0m
                        ? $"تحصيل فرق {paidTotal:0.00} ج.م ({string.Join(" + ", settlementParts)}) + ترحيل {creditGap:0.00} ج.م آجل"
                        : $"تحصيل فرق {paidTotal:0.00} ج.م ({string.Join(" + ", settlementParts)})";
                }
                else if (difference < 0m)
                {
                    settlement = $"رد للعميل {Math.Abs(difference):0.00} ج.م (استبدال)";
                }
                else
                {
                    settlement = "استبدال متساوي القيمة (0.00 ج.م)";
                }

                int operationId;
                using (var command = CreateCommand(connection, transaction, @"
INSERT dbo.ReturnExchanges(OperationNumber,SalesInvoiceId,OperationType,CreatedAtUtc,ActorUserId,Reason,SettlementSummary)
OUTPUT inserted.Id
VALUES(@Number,@Invoice,1,SYSUTCDATETIME(),@Actor,@Reason,@Settlement);"))
                {
                    command.Parameters.Add("@Number", SqlDbType.NVarChar, 50).Value = operationNumber;
                    command.Parameters.Add("@Invoice", SqlDbType.Int).Value = request.Invoice.Id;
                    command.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
                    command.Parameters.Add("@Reason", SqlDbType.NVarChar, 1000).Value = Trim(request.Reason, 1000, "");
                    command.Parameters.Add("@Settlement", SqlDbType.NVarChar, 1500).Value = Trim(settlement, 1500, "");
                    operationId = (int)command.ExecuteScalar()!;
                }

                var goodTotal = 0;
                var damagedTotal = 0;

                foreach (var line in request.Lines)
                {
                    if (line.Quantity <= 0) continue;

                    var snapshot = LoadLineSnapshot(connection, transaction, line.SalesInvoiceLineId, request.Invoice.Id);
                    if (line.Quantity > snapshot.RemainingQuantity)
                        throw new SalesDataException($"الكمية المتاحة للمرتجع من «{snapshot.ProductName}» هي {snapshot.RemainingQuantity} فقط.");

                    var goodQty = line.GoodQuantityValue;
                    var damagedQty = line.DamagedQuantity;
                    goodTotal += goodQty;
                    damagedTotal += damagedQty;

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
                        AddMoney(command, "@Price", line.UnitPrice);
                        AddMoney(command, "@Cost", line.UnitCost);
                        command.Parameters.Add("@Condition", SqlDbType.TinyInt).Value = (byte)(damagedQty > 0 ? 1 : 0);
                        returnLineId = (int)command.ExecuteScalar()!;
                    }

                    var units = ClaimSoldUnits(connection, transaction, line.SalesInvoiceLineId, line.Quantity);
                    var unitIndex = 0;
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

                        var asGood = unitIndex++ < goodQty;
                        if (asGood)
                            ReturnUnitToStock(connection, transaction, request.Invoice.Id, request.Invoice.InvoiceNumber,
                                unit.UnitId, snapshot.ProductId, unit.Cost, "Return", $"استبدال: {request.Reason}");
                        else
                            MoveUnitToDamaged(connection, transaction, request.Invoice.Id, unit.UnitId,
                                snapshot.ProductId, unit.Cost, $"استبدال فاتورة {request.Invoice.InvoiceNumber}: {request.Reason}");
                    }

                    using var updateLine = CreateCommand(connection, transaction,
                        "UPDATE dbo.SalesInvoiceLines SET ReturnedQuantity = ReturnedQuantity + @Quantity WHERE Id=@Id;");
                    updateLine.Parameters.Add("@Quantity", SqlDbType.Int).Value = line.Quantity;
                    updateLine.Parameters.Add("@Id", SqlDbType.Int).Value = line.SalesInvoiceLineId;
                    updateLine.ExecuteNonQuery();
                }

                var replacementUnits = 0;
                var nextLineNumber = NextSalesLineNumber(connection, transaction, request.Invoice.Id);

                foreach (var (replacement, product, price) in planned)
                {
                    var claimed = ClaimAvailableUnits(connection, transaction, replacement.ProductId, replacement.Quantity);
                    if (claimed.Count < replacement.Quantity)
                        throw new SalesDataException($"المتاح من الصنف البديل «{product.Name}» ({claimed.Count}) أقل من الكمية المطلوبة ({replacement.Quantity}).");

                    var lineNumber = nextLineNumber++;
                    int replacementLineId;
                    using (var command = CreateCommand(connection, transaction, @"
INSERT dbo.SalesInvoiceLines(SalesInvoiceId,LineNumber,ProductId,BarcodeSnapshot,ProductNameSnapshot,CategoryNameSnapshot,
 UnitNameSnapshot,TrackSerialNumber,PriceTier,Quantity,UnitPrice,UnitCost,IsExchangeReplacement)
OUTPUT inserted.Id
VALUES(@Invoice,@Line,@Product,@Barcode,@Name,@Category,@Unit,@TrackSerial,@Tier,@Quantity,@Price,@Cost,1);"))
                    {
                        command.Parameters.Add("@Invoice", SqlDbType.Int).Value = request.Invoice.Id;
                        command.Parameters.Add("@Line", SqlDbType.Int).Value = lineNumber;
                        command.Parameters.Add("@Product", SqlDbType.Int).Value = product.Id;
                        command.Parameters.Add("@Barcode", SqlDbType.NVarChar, 100).Value = product.Barcode;
                        command.Parameters.Add("@Name", SqlDbType.NVarChar, 250).Value = product.Name;
                        command.Parameters.Add("@Category", SqlDbType.NVarChar, 200).Value = product.Category;
                        command.Parameters.Add("@Unit", SqlDbType.NVarChar, 50).Value = product.UnitName;
                        command.Parameters.Add("@TrackSerial", SqlDbType.Bit).Value = product.TrackSerialNumber;
                        command.Parameters.Add("@Tier", SqlDbType.TinyInt).Value = (byte)Math.Clamp(replacement.PriceTier, 0, 2);
                        command.Parameters.Add("@Quantity", SqlDbType.Int).Value = replacement.Quantity;
                        AddMoney(command, "@Price", price);
                        AddMoney(command, "@Cost", product.CostPrice);
                        replacementLineId = (int)command.ExecuteScalar()!;
                    }

                    var serials = new List<string>();
                    foreach (var unit in claimed)
                    {
                        using (var link = CreateCommand(connection, transaction,
                            "INSERT dbo.SalesInvoiceLineUnits(SalesInvoiceLineId,InventoryUnitId,AllocationStatus) VALUES(@Line,@Unit,0);"))
                        {
                            link.Parameters.Add("@Line", SqlDbType.Int).Value = replacementLineId;
                            link.Parameters.Add("@Unit", SqlDbType.Int).Value = unit.Id;
                            link.ExecuteNonQuery();
                        }

                        ModifyUnit(connection, transaction, unit.Id, isSold: true);
                        StockMovement(connection, transaction, request.Invoice.Id, unit.Id, "ExchangeOut",
                            -1, product.CostPrice, $"استبدال {operationNumber}");
                    }

                    using (var rl = CreateCommand(connection, transaction, @"
INSERT dbo.ReturnExchangeLines(ReturnExchangeId,LineKind,SalesInvoiceLineId,ProductId,ProductNameSnapshot,Quantity,UnitPrice,UnitCost,UnitCondition)
VALUES(@Operation,2,NULL,@Product,@Name,@Quantity,@Price,@Cost,0);"))
                    {
                        rl.Parameters.Add("@Operation", SqlDbType.Int).Value = operationId;
                        rl.Parameters.Add("@Product", SqlDbType.Int).Value = product.Id;
                        rl.Parameters.Add("@Name", SqlDbType.NVarChar, 250).Value = product.Name;
                        rl.Parameters.Add("@Quantity", SqlDbType.Int).Value = replacement.Quantity;
                        AddMoney(rl, "@Price", price);
                        AddMoney(rl, "@Cost", product.CostPrice);
                        rl.ExecuteNonQuery();
                    }

                    replacementValue += replacement.Quantity * price;
                    replacementUnits += replacement.Quantity;
                }

                // Settlement side effects: collect the upgrade difference, or refund the customer.
                if (difference > 0m)
                    ApplyExchangePayment(connection, transaction, request.Invoice, payment);
                else if (difference < 0m)
                    RefundAccounts(connection, transaction, request.Invoice, Math.Abs(difference),
                        $"رد فرق استبدال {operationNumber}");

                using (var status = CreateCommand(connection, transaction, @"
UPDATE dbo.SalesInvoices SET InvoiceStatus=5, UpdatedAtUtc=SYSUTCDATETIME() WHERE Id=@Id;"))
                {
                    status.Parameters.Add("@Id", SqlDbType.Int).Value = request.Invoice.Id;
                    status.ExecuteNonQuery();
                }

                AddActivity(connection, transaction, request.Invoice.Id, $"عملية استبدال ({operationNumber})",
                    $"راجع: {goodTotal} سليم / {damagedTotal} هالك بقيمة {returnedValue:0.00} ج.م | بديل: {replacementUnits} وحدة بقيمة {replacementValue:0.00} ج.م | السبب: {request.Reason} | التسوية: {settlement}.",
                    Math.Max(returnedValue, replacementValue));
                Audit(connection, transaction, "CreateExchange", request.Invoice.Id,
                    $"استبدال {operationNumber} على الفاتورة {request.Invoice.InvoiceNumber}", "SalesInvoice");

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
            throw Describe(ex, "تعذر حفظ الاستبدال في قاعدة البيانات.");
        }
    }

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
       SUM(CASE WHEN l.LineKind = 1 THEN l.Quantity ELSE 0 END),
       COUNT(DISTINCT CASE WHEN l.LineKind = 1 AND l.UnitCondition = 1 THEN uu.InventoryUnitId END),
       SUM(CASE WHEN l.LineKind = 2 THEN l.Quantity ELSE 0 END),
       SUM(CASE WHEN l.LineKind = 1 THEN l.Quantity * l.UnitPrice ELSE 0 END),
       SUM(CASE WHEN l.LineKind = 2 THEN l.Quantity * l.UnitPrice ELSE 0 END)
FROM dbo.ReturnExchanges o
JOIN dbo.Users u ON u.Id = o.ActorUserId
JOIN dbo.SalesInvoices si ON si.Id = o.SalesInvoiceId
LEFT JOIN dbo.ReturnExchangeLines l ON l.ReturnExchangeId = o.Id
LEFT JOIN dbo.ReturnExchangeUnits uu ON uu.ReturnExchangeLineId = l.Id
GROUP BY o.Id, o.OperationNumber, o.OperationType, o.CreatedAtUtc, u.DisplayName, o.SalesInvoiceId,
         si.InvoiceNumber, si.CustomerNameSnapshot, si.CustomerPhoneSnapshot, o.Reason, o.SettlementSummary
ORDER BY o.CreatedAtUtc DESC, o.Id DESC;";

            using var reader = command.ExecuteReader();
            var ids = new List<int>();
            var raw = new List<(ReturnExchangeRecord Record, int Good, int Damaged)>();
            while (reader.Read())
            {
                var good = reader.IsDBNull(11) ? 0 : Convert.ToInt32(reader.GetValue(11));
                var damaged = reader.IsDBNull(12) ? 0 : Convert.ToInt32(reader.GetValue(12));
                var replacementUnits = reader.IsDBNull(13) ? 0 : Convert.ToInt32(reader.GetValue(13));
                var returnedValue = reader.IsDBNull(14) ? 0m : Convert.ToDecimal(reader.GetValue(14));
                var replacementValue = reader.IsDBNull(15) ? 0m : Convert.ToDecimal(reader.GetValue(15));

                // Good units are the returned quantity minus the damaged ones tagged on their lines.
                raw.Add((new ReturnExchangeRecord(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetByte(2),
                    AsLocal(reader.GetDateTime(3)),
                    reader.GetString(4),
                    reader.GetInt32(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.GetString(10),
                    Math.Max(0, good - damaged),
                    damaged,
                    replacementUnits,
                    returnedValue,
                    replacementValue,
                    "",
                    ""), good, damaged));

                ids.Add(reader.GetInt32(0));
            }

            var summaries = LoadOperationLineSummaries(connection, ids);
            foreach (var (record, _, _) in raw)
            {
                var summary = summaries.GetValueOrDefault(record.Id, ([], []));
                records.Add(record with
                {
                    ReturnedItemsSummary = string.Join("، ", summary.Returned),
                    ReplacementItemsSummary = summary.Replacements.Count > 0 ? string.Join("، ", summary.Replacements) : "—"
                });
            }
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            throw Describe(ex, "تعذر تحميل سجل المرتجع والاستبدال من قاعدة البيانات.");
        }

        return records;
    }

    private static Dictionary<int, (List<string> Returned, List<string> Replacements)> LoadOperationLineSummaries(
        SqlConnection connection, List<int> operationIds)
    {
        var result = new Dictionary<int, (List<string>, List<string>)>();
        if (operationIds.Count == 0) return result;

        // Small, bounded IN list built from integer ids (no user text), safe for SQL Server 2016.
        var idList = string.Join(",", operationIds.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT ReturnExchangeId, LineKind, ProductNameSnapshot, Quantity, UnitCondition
FROM dbo.ReturnExchangeLines
WHERE ReturnExchangeId IN ({idList})
ORDER BY ReturnExchangeId, Id;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var operationId = reader.GetInt32(0);
            var kind = reader.GetByte(1);
            var name = reader.GetString(2);
            var quantity = reader.GetInt32(3);
            var condition = reader.GetByte(4);

            if (!result.TryGetValue(operationId, out var entry))
            {
                entry = ([], []);
                result[operationId] = entry;
            }

            if (kind == 2)
                entry.Replacements.Add($"{quantity}× {name}");
            else
                entry.Returned.Add($"{name} ({quantity} {(condition == 1 ? "هالك" : "سليم")})");
        }

        return result;
    }

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
                        command.Parameters.Add("@Condition", SqlDbType.TinyInt).Value = (byte)(line.GoodQuantityValue < line.Quantity ? 1 : 0);
                        returnLineId = (int)command.ExecuteScalar()!;
                    }

                    var units = ClaimSoldUnits(connection, transaction, line.SalesInvoiceLineId, line.Quantity);
                    var goodRemaining = line.GoodQuantityValue;
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

                        // The first units go back to stock, the rest are booked as damaged (mixed-condition line).
                        if (goodRemaining > 0)
                        {
                            goodRemaining--;
                            ReturnUnitToStock(connection, transaction, request.Invoice.Id, request.Invoice.InvoiceNumber,
                                unit.UnitId, snapshot.ProductId, unit.Cost, "Return", $"مرتجع {request.Reason}");
                        }
                        else
                        {
                            MoveUnitToDamaged(connection, transaction, request.Invoice.Id, unit.UnitId,
                                snapshot.ProductId, unit.Cost, $"مرتجع فاتورة {request.Invoice.InvoiceNumber}: {request.Reason}");
                        }
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

                var allReturned = AllLinesReturned(connection, transaction, request.Invoice.Id);
                AddActivity(connection, transaction, request.Invoice.Id,
                    allReturned ? "مرتجع كلي" : "مرتجع جزئي",
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

    /// <summary>True when every line of the invoice is fully returned — read from SQL, not from the UI model.</summary>
    private static bool AllLinesReturned(SqlConnection connection, SqlTransaction transaction, int invoiceId)
    {
        using var command = CreateCommand(connection, transaction, @"
SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.SalesInvoiceLines WHERE SalesInvoiceId = @Invoice AND ReturnedQuantity < Quantity)
            THEN 0 ELSE 1 END;");
        command.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
        return Convert.ToInt32(command.ExecuteScalar()!) == 1;
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

    private void MoveUnitToDamaged(
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
        int accountId, decimal amount, string customerName, string? note = null, string? category = null)
    {
        if (amount <= 0m) return;

        var paymentNote = note ?? "رد مبلغ فاتورة ملغاة";
        var ledgerCategory = category ?? "رد فاتورة ملغاة";

        using (var payment = CreateCommand(connection, transaction, @"
INSERT dbo.SalesPayments(SalesInvoiceId,FinancialAccountId,PaymentKind,Amount,PaidAtUtc,Note,ActorUserId)
VALUES(@Invoice,@Account,2,@Amount,SYSUTCDATETIME(),@Note,@Actor);"))
        {
            payment.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
            payment.Parameters.Add("@Account", SqlDbType.Int).Value = accountId;
            payment.Parameters.Add("@Amount", SqlDbType.Decimal).Value = amount;
            payment.Parameters["@Amount"].Precision = 19;
            payment.Parameters["@Amount"].Scale = 2;
            payment.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
            payment.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value = Trim(paymentNote, 1000, "");
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
VALUES(@Account,SYSUTCDATETIME(),0,@Category,@Amount,@Balance,@Counterparty,@Note,@Actor,0,N'SalesInvoice',@Invoice);");
        ledger.Parameters.Add("@Account", SqlDbType.Int).Value = accountId;
        ledger.Parameters.Add("@Amount", SqlDbType.Decimal).Value = amount;
        ledger.Parameters["@Amount"].Precision = 19;
        ledger.Parameters["@Amount"].Scale = 2;
        ledger.Parameters.Add("@Balance", SqlDbType.Decimal).Value = balanceAfter;
        ledger.Parameters["@Balance"].Precision = 19;
        ledger.Parameters["@Balance"].Scale = 2;
        ledger.Parameters.Add("@Counterparty", SqlDbType.NVarChar, 250).Value = Trim(customerName, 250, "عميل نقدي");
        ledger.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value = Trim(note ?? $"إلغاء {invoiceNumber}", 1000, "");
        ledger.Parameters.Add("@Category", SqlDbType.NVarChar, 100).Value = Trim(ledgerCategory, 100, "رد فاتورة ملغاة");
        ledger.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
        ledger.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
        ledger.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ helpers

    private sealed record ClaimedUnit(int Id, decimal Cost);

    /// <summary>
    /// Locks and returns the units this line will sell. A product whose stock only exists as
    /// StockMovements rows (no registered units) sells against that balance without unit rows.
    /// </summary>
    private static List<ClaimedUnit> ClaimAvailableUnits(
        SqlConnection connection, SqlTransaction transaction, PosSaleLineRequest line)
    {
        var units = new List<ClaimedUnit>();
        var registered = 0;

        using (var command = CreateCommand(connection, transaction, @"
SELECT Id, PurchaseCost FROM (
    SELECT TOP (@Quantity) iu.Id, iu.PurchaseCost, iu.RegisteredAtUtc
    FROM dbo.InventoryUnits iu WITH (UPDLOCK, HOLDLOCK)
    WHERE iu.ProductId = @ProductId AND iu.UnitStatus = 0
    ORDER BY iu.RegisteredAtUtc, iu.Id
) AS claimed ORDER BY RegisteredAtUtc, Id;
SELECT COUNT(*) FROM dbo.InventoryUnits WITH (UPDLOCK, HOLDLOCK) WHERE ProductId = @ProductId;"))
        {
            command.Parameters.Add("@Quantity", SqlDbType.Int).Value = line.Quantity;
            command.Parameters.Add("@ProductId", SqlDbType.Int).Value = line.ProductId;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                units.Add(new ClaimedUnit(reader.GetInt32(0), reader.GetDecimal(1)));

            reader.NextResult();
            if (reader.Read()) registered = reader.GetInt32(0);
        }

        if (units.Count >= line.Quantity) return units;

        // No registered units at all: the unit-less balance is checked by the movement sum below.
        if (registered == 0 && units.Count == 0)
        {
            var balance = GetUnregisteredStockBalance(connection, transaction, line.ProductId);
            if (balance >= line.Quantity) return units;

            throw new SalesDataException(
                $"المتاح من «{line.ProductName}» هو {Math.Max(0, balance)} قطعة فقط، والمطلوب {line.Quantity}. " +
                "حدّث الأصناف من قاعدة البيانات وعدّل الكمية.");
        }

        throw new SalesDataException(
            $"المتاح من «{line.ProductName}» هو {units.Count} قطعة فقط، والمطلوب {line.Quantity}. " +
            "حدّث الأصناف من قاعدة البيانات وعدّل الكمية.");
    }

    /// <summary>Adds a money parameter with the schema precision used by every DECIMAL(19,2) column.</summary>
    private static void AddMoney(SqlCommand command, string name, decimal value)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Value = value;
        parameter.Precision = 19;
        parameter.Scale = 2;
    }

    /// <summary>Claims available units for an exchange replacement (same locking rules as a POS sale).</summary>
    private static List<ClaimedUnit> ClaimAvailableUnits(
        SqlConnection connection, SqlTransaction transaction, int productId, int quantity)
    {
        var units = new List<ClaimedUnit>();
        using var command = CreateCommand(connection, transaction, @"
SELECT Id, PurchaseCost FROM (
    SELECT TOP (@Quantity) iu.Id, iu.PurchaseCost, iu.RegisteredAtUtc
    FROM dbo.InventoryUnits iu WITH (UPDLOCK, HOLDLOCK)
    WHERE iu.ProductId = @ProductId AND iu.UnitStatus = 0
    ORDER BY iu.RegisteredAtUtc, iu.Id
) AS claimed ORDER BY RegisteredAtUtc, Id;");
        command.Parameters.Add("@Quantity", SqlDbType.Int).Value = quantity;
        command.Parameters.Add("@ProductId", SqlDbType.Int).Value = productId;

        using var reader = command.ExecuteReader();
        while (reader.Read()) units.Add(new ClaimedUnit(reader.GetInt32(0), reader.GetDecimal(1)));
        return units;
    }

    private sealed record SaleProductSnapshot(
        int Id, string Barcode, string Name, string Category, string UnitName,
        bool TrackSerialNumber, decimal CostPrice, decimal RetailPrice, decimal SpecialPrice, decimal WholesalePrice);

    private static SaleProductSnapshot LoadProductForSale(
        SqlConnection connection, SqlTransaction transaction, int productId)
    {
        using var command = CreateCommand(connection, transaction, @"
SELECT p.Id, p.Barcode, p.Name, c.Name, p.UnitName, p.TrackSerialNumber,
       p.CostPrice, p.RetailPrice, p.SpecialPrice, p.WholesalePrice
FROM dbo.Products p
JOIN dbo.Categories c ON c.Id = p.CategoryId
WHERE p.Id = @Id;");
        command.Parameters.Add("@Id", SqlDbType.Int).Value = productId;
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new SalesDataException("الصنف البديل غير موجود في قاعدة البيانات؛ اضغط «تحديث» وأعد المحاولة.");

        return new SaleProductSnapshot(
            reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
            reader.GetBoolean(5), reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8), reader.GetDecimal(9));
    }

    private static int NextSalesLineNumber(SqlConnection connection, SqlTransaction transaction, int invoiceId)
    {
        using var command = CreateCommand(connection, transaction,
            "SELECT COALESCE(MAX(LineNumber),0) + 1 FROM dbo.SalesInvoiceLines WITH (UPDLOCK, HOLDLOCK) WHERE SalesInvoiceId=@Invoice;");
        command.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
        return Convert.ToInt32(command.ExecuteScalar()!);
    }

    private void ModifyUnit(SqlConnection connection, SqlTransaction transaction, int unitId, bool isSold)
    {
        using var command = CreateCommand(connection, transaction, @"
UPDATE dbo.InventoryUnits
SET UnitStatus = 1, SoldAtUtc = SYSUTCDATETIME(), DamagedAtUtc = NULL, DamageReason = N'',
    DamageSettlementStatus = N'', SaleInvoiceNumber = N''
WHERE Id = @Id;");
        command.Parameters.Add("@Id", SqlDbType.Int).Value = unitId;
        command.ExecuteNonQuery();
    }

    private void StockMovement(
        SqlConnection connection, SqlTransaction transaction, int invoiceId, int unitId,
        string movementType, int quantityDelta, decimal unitCost, string reason)
    {
        using var command = CreateCommand(connection, transaction, @"
INSERT dbo.StockMovements(ProductId,InventoryUnitId,MovementType,QuantityDelta,UnitCost,ActorUserId,SourceEntityType,SourceEntityId,Reason)
SELECT iu.ProductId, iu.Id, @Type, @Delta, @Cost, @Actor, N'ReturnExchange', @Invoice, @Reason
FROM dbo.InventoryUnits iu WHERE iu.Id = @Unit;");
        command.Parameters.Add("@Type", SqlDbType.NVarChar, 30).Value = movementType;
        command.Parameters.Add("@Delta", SqlDbType.Int).Value = quantityDelta;
        AddMoney(command, "@Cost", unitCost);
        command.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
        command.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
        command.Parameters.Add("@Reason", SqlDbType.NVarChar, 1000).Value = Trim(reason, 1000, "");
        command.Parameters.Add("@Unit", SqlDbType.Int).Value = unitId;
        command.ExecuteNonQuery();
    }

    /// <summary>Refunds an exchange difference from one or more accounts with their ledger rows.</summary>
    private void RefundAccounts(
        SqlConnection connection, SqlTransaction transaction, SalesInvoiceDesignItem invoice,
        decimal amount, string note)
    {
        if (amount <= 0m) return;

        var accounts = EnsureAccounts(connection, transaction);
        var remaining = Math.Round(amount, 2, MidpointRounding.AwayFromZero);

        // Refund from cash first, then the wallets that actually collected money for this invoice.
        var collected = LoadCollectedAmounts(connection, transaction, invoice.Id);
        var order = new (FinancialAccountKind Kind, decimal Collected)[]
        {
            (FinancialAccountKind.Cash, collected.Cash),
            (FinancialAccountKind.VodafoneCash, collected.Vodafone),
            (FinancialAccountKind.InstaPay, collected.InstaPay)
        };

        foreach (var (kind, collectedAmount) in order)
        {
            if (remaining <= 0m) break;
            var portion = kind == FinancialAccountKind.Cash ? remaining : Math.Min(remaining, collectedAmount);
            if (portion <= 0m) continue;
            remaining -= portion;
            RefundOne(connection, transaction, invoice.Id, invoice.InvoiceNumber, accounts[kind], portion,
                Trim(invoice.CustomerName, 250, "عميل نقدي"), note, "رد فرق استبدال");
        }

        if (remaining > 0m)
            RefundOne(connection, transaction, invoice.Id, invoice.InvoiceNumber, accounts[FinancialAccountKind.Cash],
                remaining, Trim(invoice.CustomerName, 250, "عميل نقدي"), note, "رد فرق استبدال");
    }

    /// <summary>Collects the extra money a customer pays for an upgrade exchange.</summary>
    private void ApplyExchangePayment(
        SqlConnection connection, SqlTransaction transaction, SalesInvoiceDesignItem invoice, ExchangePayment payment)
    {
        var accounts = EnsureAccounts(connection, transaction);
        var customer = Trim(invoice.CustomerName, 250, "عميل نقدي");

        void Collect(FinancialAccountKind kind, decimal amount)
        {
            if (amount <= 0m) return;
            var applied = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
            if (applied <= 0m) return;

            using (var insert = CreateCommand(connection, transaction, @"
INSERT dbo.SalesPayments(SalesInvoiceId,FinancialAccountId,PaymentKind,Amount,PaidAtUtc,Note,ActorUserId)
VALUES(@Invoice,@Account,1,@Amount,SYSUTCDATETIME(),N'تحصيل فرق استبدال',@Actor);"))
            {
                insert.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoice.Id;
                insert.Parameters.Add("@Account", SqlDbType.Int).Value = accounts[kind];
                AddMoney(insert, "@Amount", applied);
                insert.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
                insert.ExecuteNonQuery();
            }

            decimal balanceAfter;
            using (var balance = CreateCommand(connection, transaction, @"
UPDATE dbo.FinancialAccounts SET CurrentBalance = CurrentBalance + @Amount, UpdatedAtUtc = SYSUTCDATETIME()
OUTPUT inserted.CurrentBalance WHERE Id = @Id;"))
            {
                AddMoney(balance, "@Amount", applied);
                balance.Parameters.Add("@Id", SqlDbType.Int).Value = accounts[kind];
                balanceAfter = (decimal)balance.ExecuteScalar()!;
            }

            using var ledger = CreateCommand(connection, transaction, @"
INSERT dbo.FinancialTransactions(FinancialAccountId,OccurredAtUtc,IsIncoming,Category,Amount,BalanceAfter,Counterparty,Note,
 ActorUserId,CountsTowardLimits,SourceEntityType,SourceEntityId)
VALUES(@Account,SYSUTCDATETIME(),1,N'فرق استبدال',@Amount,@Balance,@Counterparty,@Note,@Actor,@Limits,N'SalesInvoice',@Invoice);");
            ledger.Parameters.Add("@Account", SqlDbType.Int).Value = accounts[kind];
            AddMoney(ledger, "@Amount", applied);
            AddMoney(ledger, "@Balance", balanceAfter);
            ledger.Parameters.Add("@Counterparty", SqlDbType.NVarChar, 250).Value = customer;
            ledger.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value =
                $"فرق استبدال فاتورة {invoice.InvoiceNumber}";
            ledger.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
            ledger.Parameters.Add("@Limits", SqlDbType.Bit).Value = kind != FinancialAccountKind.Cash;
            ledger.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoice.Id;
            ledger.ExecuteNonQuery();
        }

        Collect(FinancialAccountKind.Cash, payment.Cash);
        Collect(FinancialAccountKind.VodafoneCash, payment.Vodafone);
        Collect(FinancialAccountKind.InstaPay, payment.InstaPay);
    }

    /// <summary>Sum of StockMovements for a product that has no registered units.</summary>
    private static int GetUnregisteredStockBalance(
        SqlConnection connection, SqlTransaction transaction, int productId)
    {
        using var command = CreateCommand(connection, transaction, @"
SELECT COALESCE(SUM(m.QuantityDelta), 0) FROM dbo.StockMovements m WHERE m.ProductId = @ProductId;");
        command.Parameters.Add("@ProductId", SqlDbType.Int).Value = productId;
        return Convert.ToInt32(command.ExecuteScalar()!);
    }

    /// <summary>Records the sold quantity of a product that has no unit rows against its movement ledger.</summary>
    private void RecordUntrackedSale(
        SqlConnection connection, SqlTransaction transaction, int invoiceId, string invoiceNumber,
        PosSaleLineRequest line, int quantity, decimal unitCost)
    {
        using var movement = CreateCommand(connection, transaction, @"
INSERT dbo.StockMovements(ProductId,InventoryUnitId,MovementType,QuantityDelta,UnitCost,ActorUserId,SourceEntityType,SourceEntityId,Reason)
VALUES(@Product,NULL,N'Sale',-@Quantity,@Cost,@Actor,N'SalesInvoice',@Invoice,@Reason);");
        movement.Parameters.Add("@Product", SqlDbType.Int).Value = line.ProductId;
        movement.Parameters.Add("@Quantity", SqlDbType.Int).Value = quantity;
        movement.Parameters.Add("@Cost", SqlDbType.Decimal).Value = unitCost;
        movement.Parameters["@Cost"].Precision = 19;
        movement.Parameters["@Cost"].Scale = 2;
        movement.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
        movement.Parameters.Add("@Invoice", SqlDbType.Int).Value = invoiceId;
        movement.Parameters.Add("@Reason", SqlDbType.NVarChar, 1000).Value =
            Trim($"بيع من نقطة البيع بدون تسجيل وحدات — {invoiceNumber}", 1000, "");
        movement.ExecuteNonQuery();
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
