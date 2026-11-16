using System.Data;
using Microsoft.Data.SqlClient;
using ProjectZ.Services;

namespace ProjectZ.Data;

/// <summary>User-facing failure raised by the expense-category layer; the message is safe to show in the UI.</summary>
public sealed class ExpenseCategoryDataException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

/// <summary>One row of dbo.ExpenseCategories.</summary>
public sealed class ExpenseCategoryRecord
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
}

/// <summary>
/// SQL Server persistence for «تصنيفات المصروفات»: the expense sections («إيجار»، «فواتير»…)
/// that the expense screens and the expense reports will read later.
/// </summary>
public sealed class ExpenseCategoryRepository(ConnectionSettingsProvider settings, int actorUserId)
{
    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 500;
    private const string MissingTableMessage =
        "جدول تصنيفات المصروفات غير موجود في قاعدة البيانات. شغّل sql/19_EnableExpenseCategories.sql على الخادم ثم أعد المحاولة.";

    // -------------------------------------------------------------- read side

    /// <summary>Loads every expense section ordered by name.</summary>
    public List<ExpenseCategoryRecord> LoadAll()
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
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not ExpenseCategoryDataException))
        {
            throw Describe(ex, "تعذر تحميل تصنيفات المصروفات من قاعدة البيانات.");
        }

        return rows;
    }

    // ------------------------------------------------------------- write side

    public ExpenseCategoryRecord Create(string name, string description)
    {
        var trimmedName = NormalizeName(name);
        var trimmedDescription = NormalizeDescription(description);

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                EnsureNameIsFree(connection, transaction, trimmedName, exceptId: null);

                int id;
                using (var command = CreateCommand(connection, transaction, @"
INSERT dbo.ExpenseCategories(Name,Description)
OUTPUT inserted.Id
VALUES(@Name,@Description);"))
                {
                    command.Parameters.Add("@Name", SqlDbType.NVarChar, MaxNameLength).Value = trimmedName;
                    command.Parameters.Add("@Description", SqlDbType.NVarChar, MaxDescriptionLength).Value = trimmedDescription;
                    id = (int)command.ExecuteScalar()!;
                }

                Audit(connection, transaction, "CreateExpenseCategory", id, $"إضافة تصنيف مصروف {trimmedName}");
                transaction.Commit();
                return new ExpenseCategoryRecord { Id = id, Name = trimmedName, Description = trimmedDescription };
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not ExpenseCategoryDataException))
        {
            throw Describe(ex, "تعذر حفظ تصنيف المصروف في قاعدة البيانات.");
        }
    }

    /// <summary>Renames a section and edits its description in place.</summary>
    public ExpenseCategoryRecord Rename(int id, string name, string description)
    {
        var trimmedName = NormalizeName(name);
        var trimmedDescription = NormalizeDescription(description);

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var current = LoadOne(connection, transaction, id);
                EnsureNameIsFree(connection, transaction, trimmedName, exceptId: id);

                using (var command = CreateCommand(connection, transaction, @"
UPDATE dbo.ExpenseCategories SET Name=@Name, Description=@Description, UpdatedAtUtc=SYSUTCDATETIME()
WHERE Id=@Id;"))
                {
                    command.Parameters.Add("@Name", SqlDbType.NVarChar, MaxNameLength).Value = trimmedName;
                    command.Parameters.Add("@Description", SqlDbType.NVarChar, MaxDescriptionLength).Value = trimmedDescription;
                    command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                    if (command.ExecuteNonQuery() != 1)
                        throw new ExpenseCategoryDataException(
                            "التصنيف غير موجود في قاعدة البيانات؛ حدّث الشاشة وأعد المحاولة.");
                }

                Audit(connection, transaction, "RenameExpenseCategory", id,
                    $"تعديل تصنيف المصروف من {current.Name} إلى {trimmedName}");
                transaction.Commit();
                return new ExpenseCategoryRecord { Id = id, Name = trimmedName, Description = trimmedDescription };
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not ExpenseCategoryDataException))
        {
            throw Describe(ex, "تعذر تعديل تصنيف المصروف في قاعدة البيانات.");
        }
    }

    /// <summary>
    /// Deletes a section. The database refuses the delete once an expense row references it
    /// (foreign key 547), so the history of the reports stays readable.
    /// </summary>
    public void Delete(int id)
    {
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var current = LoadOne(connection, transaction, id);

                using (var command = CreateCommand(connection, transaction,
                    "DELETE dbo.ExpenseCategories WHERE Id=@Id;"))
                {
                    command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                    if (command.ExecuteNonQuery() != 1)
                        throw new ExpenseCategoryDataException(
                            "التصنيف غير موجود في قاعدة البيانات؛ حدّث الشاشة وأعد المحاولة.");
                }

                Audit(connection, transaction, "DeleteExpenseCategory", id,
                    $"حذف تصنيف مصروف {current.Name}");
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not ExpenseCategoryDataException))
        {
            throw Describe(ex, "تعذر حذف تصنيف المصروف من قاعدة البيانات.");
        }
    }

    // ---------------------------------------------------------------- helpers

    private static ExpenseCategoryRecord LoadOne(SqlConnection connection, SqlTransaction transaction, int id)
    {
        using var command = CreateCommand(connection, transaction, @"
SELECT Id, Name, Description
FROM dbo.ExpenseCategories
WHERE Id=@Id;");
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new ExpenseCategoryDataException(
                "التصنيف غير موجود في قاعدة البيانات؛ حدّث الشاشة وأعد المحاولة.");

        return new ExpenseCategoryRecord
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Description = reader.GetString(2)
        };
    }

    /// <summary>Names are compared case-insensitively so «إيجار» and «إيجار » cannot both exist.</summary>
    private static void EnsureNameIsFree(SqlConnection connection, SqlTransaction transaction, string name, int? exceptId)
    {
        using var command = CreateCommand(connection, transaction, @"
SELECT TOP 1 Name FROM dbo.ExpenseCategories WITH (UPDLOCK, HOLDLOCK)
WHERE Name = @Name AND (@ExceptId IS NULL OR Id <> @ExceptId);");
        command.Parameters.Add("@Name", SqlDbType.NVarChar, MaxNameLength).Value = name;
        command.Parameters.Add("@ExceptId", SqlDbType.Int).Value = (object?)exceptId ?? DBNull.Value;

        if (command.ExecuteScalar() is string existing)
            throw new ExpenseCategoryDataException($"تصنيف المصروف «{existing}» موجود بالفعل. اختر اسمًا مختلفًا.");
    }

    private static string NormalizeName(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ExpenseCategoryDataException("يرجى إدخال اسم تصنيف المصروف أولًا.");
        if (trimmed.Length > MaxNameLength)
            throw new ExpenseCategoryDataException($"اسم تصنيف المصروف يجب ألا يتجاوز {MaxNameLength} حرفًا.");
        if (trimmed.Contains('\n') || trimmed.Contains('\r'))
            throw new ExpenseCategoryDataException("اسم تصنيف المصروف يجب أن يكون في سطر واحد.");
        return trimmed;
    }

    private static string NormalizeDescription(string description)
    {
        var trimmed = (description ?? "").Trim();
        if (trimmed.Length > MaxDescriptionLength)
            throw new ExpenseCategoryDataException(
                $"وصف تصنيف المصروف يجب ألا يتجاوز {MaxDescriptionLength} حرفًا.");
        return trimmed;
    }

    private void Audit(SqlConnection connection, SqlTransaction transaction, string action, int entityId, string details)
    {
        using var command = CreateCommand(connection, transaction, @"
INSERT dbo.AuditLog(ActorUserId,Module,ActionName,EntityType,EntityId,NewValuesJson)
VALUES(@Actor,N'ExpenseCategories',@Action,N'ExpenseCategory',@EntityId,JSON_MODIFY(N'{}',N'$.details',@Details));");
        command.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
        command.Parameters.Add("@Action", SqlDbType.NVarChar, 150).Value = action;
        command.Parameters.Add("@EntityId", SqlDbType.NVarChar, 100).Value = entityId.ToString();
        command.Parameters.Add("@Details", SqlDbType.NVarChar, 1000).Value = details;
        command.ExecuteNonQuery();
    }

    private static ExpenseCategoryDataException Describe(Exception exception, string fallback)
    {
        if (exception is ExpenseCategoryDataException data) return data;
        if (exception is SqlException sql)
        {
            return sql.Number switch
            {
                208 => new ExpenseCategoryDataException(MissingTableMessage, sql),
                2601 or 2627 => new ExpenseCategoryDataException(
                    "اسم تصنيف المصروف مسجل بالفعل. اختر اسمًا مختلفًا.", sql),
                547 => new ExpenseCategoryDataException(
                    "تصنيف المصروف مرتبط بمصروفات مسجلة ولا يمكن حذفه. غيّر تصنيف تلك المصروفات أولًا.",
                    sql),
                1205 => new ExpenseCategoryDataException(
                    "تعارضت العملية مع مستخدم آخر. حدّث البيانات ثم أعد المحاولة.", sql),
                229 or 230 => new ExpenseCategoryDataException(
                    "حساب الاتصال لا يملك صلاحية تعديل تصنيفات المصروفات. شغّل sql/19_EnableExpenseCategories.sql على الخادم ثم أعد المحاولة.",
                    sql),
                >= 50000 => new ExpenseCategoryDataException(sql.Message, sql),
                _ => new ExpenseCategoryDataException(fallback, sql)
            };
        }

        return new ExpenseCategoryDataException(fallback, exception);
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
