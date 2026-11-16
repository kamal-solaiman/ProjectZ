using System.Data;
using Microsoft.Data.SqlClient;
using ProjectZ.Services;

namespace ProjectZ.Data;

/// <summary>User-facing failure raised by the expense layer; the message is safe to show in the UI.</summary>
public sealed class ExpenseDataException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

/// <summary>One payment line: part of the expense paid from one financial account.</summary>
public sealed record ExpensePaymentRequest(int FinancialAccountId, decimal Amount);

/// <summary>One expense as the screen submits it: one section, one total and one or more payment lines.</summary>
public sealed record SaveExpenseRequest(
    int ExpenseCategoryId,
    DateTime ExpenseDate,
    decimal Amount,
    string Description,
    IReadOnlyList<ExpensePaymentRequest> Payments);

/// <summary>One stored payment line plus the account balance after its deduction.</summary>
public sealed record SavedExpensePayment(
    int FinancialAccountId,
    string AccountAlias,
    decimal Amount,
    decimal BalanceAfter);

/// <summary>The stored expense plus every payment line that was written.</summary>
public sealed record SavedExpense(
    int ExpenseId,
    string ExpenseNumber,
    decimal Amount,
    IReadOnlyList<SavedExpensePayment> Payments);

/// <summary>
/// SQL Server persistence for «اضافة مصروف»: writes the expense row, its split payments,
/// one deduction and one ledger movement per used account, and the audit row — all inside a
/// single Serializable transaction.
/// </summary>
public sealed class ExpenseRepository(ConnectionSettingsProvider settings, int actorUserId)
{
    private const string ExpenseSequence = "Expense";
    private const int MaxPayments = 8;
    private const string MissingTableMessage =
        "جداول المصروفات غير موجودة في قاعدة البيانات. شغّل sql/20_EnableExpenses.sql " +
        "ثم sql/21_EnableExpenseSplitPayments.sql على الخادم وأعد المحاولة.";

    // -------------------------------------------------------------- read side

    /// <summary>Loads the expense sections that the category picker shows, ordered by name.</summary>
    public List<ExpenseCategoryRecord> LoadCategories()
    {
        var rows = new List<ExpenseCategoryRecord>();
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT Id, Name, Description
FROM dbo.ExpenseCategories
ORDER BY Name;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new ExpenseCategoryRecord
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Description = reader.GetString(2)
                });
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not ExpenseDataException))
        {
            throw Describe(ex, "تعذر تحميل تصنيفات المصروفات من قاعدة البيانات.");
        }

        return rows;
    }

    // ------------------------------------------------------------- write side

    /// <summary>
    /// Records one expense paid through one or more accounts. Every payment line deducts its
    /// account and appends its own ledger movement; the whole set commits or rolls back together.
    /// </summary>
    public SavedExpense Save(SaveExpenseRequest request)
    {
        var amount = Math.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0m)
            throw new ExpenseDataException("المبلغ يجب أن يكون أكبر من الصفر.");

        var expenseDate = request.ExpenseDate.Date;
        if (expenseDate > DateTime.Today)
            throw new ExpenseDataException("لا يمكن تسجيل مصروف بتاريخ مستقبلي. اختر تاريخ اليوم أو تاريخًا سابقًا.");

        var description = Trim(request.Description, 1000);
        var payments = NormalizePayments(request.Payments, amount);
        var documentYear = DateTime.Now.Year;

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var categoryName = LoadCategoryName(connection, transaction, request.ExpenseCategoryId);

                // Lock the accounts in a deterministic order so two cashiers cannot deadlock each
                // other, and refuse the whole expense when any single account cannot cover its part.
                var plans = new List<(int AccountId, string Alias, decimal Amount)>(payments.Count);
                foreach (var payment in payments.OrderBy(p => p.FinancialAccountId))
                {
                    var (alias, balance) = LoadAccountForUpdate(connection, transaction, payment.FinancialAccountId);
                    if (payment.Amount > balance)
                    {
                        throw new ExpenseDataException(
                            $"المبلغ المخصص لحساب «{alias}» ({payment.Amount:0.00}) أكبر من رصيده المتاح ({balance:0.00}). " +
                            "عدّل التوزيع أو اختر حسابًا آخر ثم أعد المحاولة.");
                    }

                    plans.Add((payment.FinancialAccountId, alias, payment.Amount));
                }

                var expenseNumber = NextDocumentNumber(connection, transaction, ExpenseSequence, documentYear, "EXP");

                int expenseId;
                using (var insert = CreateCommand(connection, transaction, @"
INSERT dbo.Expenses(ExpenseNumber,ExpenseCategoryId,ExpenseDate,Amount,Description,ActorUserId)
OUTPUT inserted.Id
VALUES(@Number,@Category,@ExpenseDate,@Amount,@Description,@Actor);"))
                {
                    insert.Parameters.Add("@Number", SqlDbType.NVarChar, 50).Value = expenseNumber;
                    insert.Parameters.Add("@Category", SqlDbType.Int).Value = request.ExpenseCategoryId;
                    insert.Parameters.Add("@ExpenseDate", SqlDbType.Date).Value = expenseDate;
                    AddMoney(insert, "@Amount", amount);
                    insert.Parameters.Add("@Description", SqlDbType.NVarChar, 1000).Value = description;
                    insert.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
                    expenseId = (int)insert.ExecuteScalar()!;
                }

                var savedPayments = new List<SavedExpensePayment>(plans.Count);
                foreach (var plan in plans)
                {
                    using (var payment = CreateCommand(connection, transaction, @"
INSERT dbo.ExpensePayments(ExpenseId,FinancialAccountId,Amount)
VALUES(@Expense,@Account,@Amount);"))
                    {
                        payment.Parameters.Add("@Expense", SqlDbType.Int).Value = expenseId;
                        payment.Parameters.Add("@Account", SqlDbType.Int).Value = plan.AccountId;
                        AddMoney(payment, "@Amount", plan.Amount);
                        payment.ExecuteNonQuery();
                    }

                    decimal balanceAfter;
                    using (var balance = CreateCommand(connection, transaction, @"
UPDATE dbo.FinancialAccounts SET CurrentBalance = CurrentBalance - @Amount, UpdatedAtUtc = SYSUTCDATETIME()
OUTPUT inserted.CurrentBalance
WHERE Id = @Id AND IsActive = 1;"))
                    {
                        AddMoney(balance, "@Amount", plan.Amount);
                        balance.Parameters.Add("@Id", SqlDbType.Int).Value = plan.AccountId;
                        var result = balance.ExecuteScalar();
                        if (result is null)
                            throw new ExpenseDataException(
                                "أحد الحسابات المالية المحددة غير موجود أو غير نشط. حدّث الشاشة وأعد المحاولة.");
                        balanceAfter = (decimal)result;
                    }

                    using (var ledger = CreateCommand(connection, transaction, @"
INSERT dbo.FinancialTransactions(FinancialAccountId,OccurredAtUtc,IsIncoming,Category,Amount,BalanceAfter,Counterparty,Note,
 ActorUserId,CountsTowardLimits,SourceEntityType,SourceEntityId)
VALUES(@Account,SYSUTCDATETIME(),0,@Category,@Amount,@Balance,N'',@Note,@Actor,0,N'Expense',@Expense);"))
                    {
                        ledger.Parameters.Add("@Account", SqlDbType.Int).Value = plan.AccountId;
                        ledger.Parameters.Add("@Category", SqlDbType.NVarChar, 150).Value = Trim(categoryName, 150);
                        AddMoney(ledger, "@Amount", plan.Amount);
                        AddMoney(ledger, "@Balance", balanceAfter);
                        ledger.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value =
                            string.IsNullOrWhiteSpace(description) ? $"مصروف {expenseNumber}" : description;
                        ledger.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
                        ledger.Parameters.Add("@Expense", SqlDbType.Int).Value = expenseId;
                        ledger.ExecuteNonQuery();
                    }

                    savedPayments.Add(new SavedExpensePayment(plan.AccountId, plan.Alias, plan.Amount, balanceAfter));
                }

                Audit(connection, transaction, expenseId, expenseNumber, amount, categoryName, savedPayments);
                transaction.Commit();
                return new SavedExpense(expenseId, expenseNumber, amount, savedPayments);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not ExpenseDataException))
        {
            throw Describe(ex, "تعذر حفظ المصروف في قاعدة البيانات.");
        }
    }

    // ---------------------------------------------------------------- helpers

    private static List<ExpensePaymentRequest> NormalizePayments(
        IReadOnlyList<ExpensePaymentRequest>? payments, decimal amount)
    {
        if (payments is null || payments.Count == 0)
            throw new ExpenseDataException("أضف وسيلة دفع واحدة على الأقل؛ لا يُحفظ مصروف بلا حساب مالي.");

        if (payments.Count > MaxPayments)
            throw new ExpenseDataException($"لا يمكن استخدام أكثر من {MaxPayments} وسائل دفع في المصروف الواحد.");

        var normalized = new List<ExpensePaymentRequest>(payments.Count);
        var seenAccounts = new HashSet<int>();
        foreach (var payment in payments)
        {
            var value = Math.Round(payment.Amount, 2, MidpointRounding.AwayFromZero);
            if (value <= 0m)
                throw new ExpenseDataException("كل وسيلة دفع يجب أن يكون مبلغها أكبر من الصفر.");

            if (!seenAccounts.Add(payment.FinancialAccountId))
                throw new ExpenseDataException("لا يمكن استخدام الحساب المالي نفسه في أكثر من وسيلة دفع. اجمع مبلغه في سطر واحد.");

            normalized.Add(payment with { Amount = value });
        }

        var total = normalized.Sum(p => p.Amount);
        if (total != amount)
        {
            throw new ExpenseDataException(
                $"مجموع وسائل الدفع ({total:0.00}) لا يساوي مبلغ المصروف ({amount:0.00}). " +
                "وزّع كامل المبلغ على وسائل الدفع قبل الحفظ.");
        }

        return normalized;
    }

    private static string LoadCategoryName(SqlConnection connection, SqlTransaction transaction, int categoryId)
    {
        using var command = CreateCommand(connection, transaction,
            "SELECT Name FROM dbo.ExpenseCategories WHERE Id = @Id;");
        command.Parameters.Add("@Id", SqlDbType.Int).Value = categoryId;
        if (command.ExecuteScalar() is string name) return name;

        throw new ExpenseDataException(
            "تصنيف المصروف المحدد لم يعد موجودًا في قاعدة البيانات. حدّث الشاشة واختر تصنيفًا آخر.");
    }

    private static (string Alias, decimal Balance) LoadAccountForUpdate(
        SqlConnection connection, SqlTransaction transaction, int accountId)
    {
        using var command = CreateCommand(connection, transaction, @"
SELECT Alias, CurrentBalance FROM dbo.FinancialAccounts WITH (UPDLOCK, HOLDLOCK)
WHERE Id = @Id AND IsActive = 1;");
        command.Parameters.Add("@Id", SqlDbType.Int).Value = accountId;
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new ExpenseDataException(
                "أحد الحسابات المالية المحددة غير موجود أو غير نشط. حدّث الشاشة واختر حسابًا آخر.");

        return (reader.GetString(0), reader.GetDecimal(1));
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

    private void Audit(
        SqlConnection connection, SqlTransaction transaction, int expenseId, string expenseNumber,
        decimal amount, string categoryName, IReadOnlyList<SavedExpensePayment> payments)
    {
        var split = string.Join(" + ", payments.Select(p => $"{p.AccountAlias} {p.Amount:0.00}"));
        using var command = CreateCommand(connection, transaction, @"
INSERT dbo.AuditLog(ActorUserId,Module,ActionName,EntityType,EntityId,NewValuesJson)
VALUES(@Actor,N'Expenses',N'CreateExpense',N'Expense',@EntityId,JSON_MODIFY(N'{}',N'$.details',@Details));");
        command.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
        command.Parameters.Add("@EntityId", SqlDbType.NVarChar, 100).Value = expenseId.ToString();
        command.Parameters.Add("@Details", SqlDbType.NVarChar, 1000).Value = Trim(
            $"تسجيل مصروف {expenseNumber} بقيمة {amount:0.00} — {categoryName} — وسائل الدفع: {split}",
            1000);
        command.ExecuteNonQuery();
    }

    private static void AddMoney(SqlCommand command, string name, decimal value)
    {
        command.Parameters.Add(name, SqlDbType.Decimal).Value = value;
        command.Parameters[name].Precision = 19;
        command.Parameters[name].Scale = 2;
    }

    private static string Trim(string? value, int max)
    {
        var trimmed = (value ?? "").Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private static ExpenseDataException Describe(Exception exception, string fallback)
    {
        if (exception is ExpenseDataException data) return data;
        if (exception is SqlException sql)
        {
            return sql.Number switch
            {
                208 => new ExpenseDataException(MissingTableMessage, sql),
                547 => new ExpenseDataException(
                    "التصنيف أو أحد الحسابات المالية المرتبطة بالمصروف غير موجود. حدّث الشاشة واختر قيمة صحيحة.", sql),
                2601 or 2627 => new ExpenseDataException(
                    "تعذّر توليد رقم مصروف فريد. أعد المحاولة بعد لحظات.", sql),
                1205 => new ExpenseDataException(
                    "تعارضت العملية مع مستخدم آخر. حدّث البيانات ثم أعد المحاولة.", sql),
                229 or 230 => new ExpenseDataException(
                    "حساب الاتصال لا يملك صلاحية تسجيل المصروفات. شغّل sql/20_EnableExpenses.sql " +
                    "ثم sql/21_EnableExpenseSplitPayments.sql على الخادم ثم أعد المحاولة.",
                    sql),
                >= 50000 => new ExpenseDataException(sql.Message, sql),
                _ => new ExpenseDataException(fallback, sql)
            };
        }

        return new ExpenseDataException(fallback, exception);
    }

    private SqlConnection OpenConnection()
    {
        var connection = new SqlConnection(settings.GetConnectionString());
        connection.Open();
        return connection;
    }

    private static SqlCommand CreateCommand(SqlConnection connection, SqlTransaction transaction, string sql) =>
        new(sql, connection, transaction) { CommandTimeout = 30 };
}
