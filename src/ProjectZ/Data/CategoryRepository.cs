using System.Collections.ObjectModel;
using System.Data;
using Microsoft.Data.SqlClient;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Data;

/// <summary>User-facing failure raised by the category layer; the message is safe to show in the UI.</summary>
public sealed class CategoryDataException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

/// <summary>A category row as stored in dbo.Categories.</summary>
public sealed class CategoryRecord
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>Products currently linked to the category (archived ones included, so the count never lies).</summary>
    public int ProductsCount { get; set; }

    /// <summary>Active products of the category, the ones shown in the sales catalog.</summary>
    public int ActiveProductsCount { get; set; }
}

/// <summary>SQL Server persistence for the categories screen and the category pickers.</summary>
public sealed class CategoryRepository(ConnectionSettingsProvider settings, int actorUserId)
{
    private const int MaxNameLength = 200;

    // -------------------------------------------------------------- read side

    /// <summary>Loads every category with its product counts, ordered by name.</summary>
    public List<CategoryRecord> LoadAll()
    {
        var rows = new List<CategoryRecord>();
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT c.Id, c.Name, c.Description,
       (SELECT COUNT(*) FROM dbo.Products p WHERE p.CategoryId = c.Id),
       (SELECT COUNT(*) FROM dbo.Products p WHERE p.CategoryId = c.Id AND p.IsActiveForSale = 1)
FROM dbo.Categories c
WHERE c.IsActive = 1
ORDER BY c.Name;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new CategoryRecord
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Description = reader.GetString(2),
                    ProductsCount = reader.GetInt32(3),
                    ActiveProductsCount = reader.GetInt32(4)
                });
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not CategoryDataException))
        {
            throw Describe(ex, "تعذر تحميل التصنيفات من قاعدة البيانات.");
        }

        return rows;
    }

    /// <summary>Fills a shared category-name list (the add-product combo box source) from SQL.</summary>
    public void LoadNamesInto(ObservableCollection<string> target)
    {
        var names = LoadAll().Select(c => c.Name).ToList();

        target.Clear();
        foreach (var name in names)
            target.Add(name);
    }

    // ------------------------------------------------------------- write side

    public CategoryRecord Create(string name, string description)
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
INSERT dbo.Categories(Name,Description,IsActive)
OUTPUT inserted.Id
VALUES(@Name,@Description,1);"))
                {
                    command.Parameters.Add("@Name", SqlDbType.NVarChar, MaxNameLength).Value = trimmedName;
                    command.Parameters.Add("@Description", SqlDbType.NVarChar, 500).Value = trimmedDescription;
                    id = (int)command.ExecuteScalar()!;
                }

                Audit(connection, transaction, "CreateCategory", id, $"إضافة تصنيف {trimmedName}");
                transaction.Commit();
                return new CategoryRecord { Id = id, Name = trimmedName, Description = trimmedDescription };
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not CategoryDataException))
        {
            throw Describe(ex, "تعذر حفظ التصنيف في قاعدة البيانات.");
        }
    }

    /// <summary>Renames a category in place; products follow automatically because they point at the row id.</summary>
    public CategoryRecord Rename(int id, string name, string description)
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
UPDATE dbo.Categories SET Name=@Name, Description=@Description, UpdatedAtUtc=SYSUTCDATETIME()
WHERE Id=@Id;"))
                {
                    command.Parameters.Add("@Name", SqlDbType.NVarChar, MaxNameLength).Value = trimmedName;
                    command.Parameters.Add("@Description", SqlDbType.NVarChar, 500).Value = trimmedDescription;
                    command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                    if (command.ExecuteNonQuery() != 1)
                        throw new CategoryDataException("التصنيف غير موجود في قاعدة البيانات؛ حدّث الشاشة وأعد المحاولة.");
                }

                Audit(connection, transaction, "RenameCategory", id,
                    $"تعديل التصنيف من {current.Name} إلى {trimmedName}");
                transaction.Commit();
                return new CategoryRecord
                {
                    Id = id,
                    Name = trimmedName,
                    Description = trimmedDescription,
                    ProductsCount = current.ProductsCount,
                    ActiveProductsCount = current.ActiveProductsCount
                };
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not CategoryDataException))
        {
            throw Describe(ex, "تعذر تعديل التصنيف في قاعدة البيانات.");
        }
    }

    /// <summary>
    /// Deletes an unused category. A category that still has products is refused with the product count,
    /// because the products table requires a category and the history must stay readable.
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
                if (current.ProductsCount > 0)
                {
                    throw new CategoryDataException(
                        $"لا يمكن حذف التصنيف «{current.Name}» لأنه مستخدم في {current.ProductsCount} صنف. " +
                        "انقل هذه الأصناف إلى تصنيف آخر أولًا ثم أعد المحاولة.");
                }

                int children;
                using (var childrenCommand = CreateCommand(connection, transaction,
                    "SELECT COUNT(*) FROM dbo.Categories WHERE ParentCategoryId=@Id;"))
                {
                    childrenCommand.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                    children = Convert.ToInt32(childrenCommand.ExecuteScalar()!);
                }

                if (children > 0)
                    throw new CategoryDataException(
                        $"لا يمكن حذف التصنيف «{current.Name}» لوجود {children} تصنيف فرعي مرتبط به.");

                using (var command = CreateCommand(connection, transaction,
                    "DELETE dbo.Categories WHERE Id=@Id;"))
                {
                    command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                    if (command.ExecuteNonQuery() != 1)
                        throw new CategoryDataException("التصنيف غير موجود في قاعدة البيانات؛ حدّث الشاشة وأعد المحاولة.");
                }

                Audit(connection, transaction, "DeleteCategory", id, $"حذف تصنيف {current.Name}");
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not CategoryDataException))
        {
            throw Describe(ex, "تعذر حذف التصنيف من قاعدة البيانات.");
        }
    }

    // ---------------------------------------------------------------- helpers

    private static CategoryRecord LoadOne(SqlConnection connection, SqlTransaction transaction, int id)
    {
        using var command = CreateCommand(connection, transaction, @"
SELECT c.Id, c.Name, c.Description,
       (SELECT COUNT(*) FROM dbo.Products p WHERE p.CategoryId = c.Id),
       (SELECT COUNT(*) FROM dbo.Products p WHERE p.CategoryId = c.Id AND p.IsActiveForSale = 1)
FROM dbo.Categories c
WHERE c.Id = @Id AND c.IsActive = 1;");
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new CategoryDataException("التصنيف غير موجود في قاعدة البيانات؛ حدّث الشاشة وأعد المحاولة.");

        return new CategoryRecord
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Description = reader.GetString(2),
            ProductsCount = reader.GetInt32(3),
            ActiveProductsCount = reader.GetInt32(4)
        };
    }

    /// <summary>Names are compared case-insensitively so «هواتف» and «هواتف » cannot both exist.</summary>
    private static void EnsureNameIsFree(SqlConnection connection, SqlTransaction transaction, string name, int? exceptId)
    {
        using var command = CreateCommand(connection, transaction, @"
SELECT TOP 1 Name FROM dbo.Categories WITH (UPDLOCK, HOLDLOCK)
WHERE Name = @Name AND (@ExceptId IS NULL OR Id <> @ExceptId);");
        command.Parameters.Add("@Name", SqlDbType.NVarChar, MaxNameLength).Value = name;
        command.Parameters.Add("@ExceptId", SqlDbType.Int).Value = (object?)exceptId ?? DBNull.Value;

        if (command.ExecuteScalar() is string existing)
            throw new CategoryDataException($"التصنيف «{existing}» موجود بالفعل. اختر اسمًا مختلفًا.");
    }

    private static string NormalizeName(string name)
    {
        var trimmed = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new CategoryDataException("يرجى إدخال اسم التصنيف أولًا.");
        if (trimmed.Length > MaxNameLength)
            throw new CategoryDataException($"اسم التصنيف يجب ألا يتجاوز {MaxNameLength} حرفًا.");
        if (trimmed.Contains('\n') || trimmed.Contains('\r'))
            throw new CategoryDataException("اسم التصنيف يجب أن يكون في سطر واحد.");
        return trimmed;
    }

    private static string NormalizeDescription(string description)
    {
        var trimmed = (description ?? "").Trim();
        if (trimmed.Length > 500)
            throw new CategoryDataException("وصف التصنيف يجب ألا يتجاوز 500 حرف.");
        return trimmed;
    }

    private void Audit(SqlConnection connection, SqlTransaction transaction, string action, int entityId, string details)
    {
        using var command = CreateCommand(connection, transaction, @"
INSERT dbo.AuditLog(ActorUserId,Module,ActionName,EntityType,EntityId,NewValuesJson)
VALUES(@Actor,N'Categories',@Action,N'Category',@EntityId,JSON_MODIFY(N'{}',N'$.details',@Details));");
        command.Parameters.Add("@Actor", SqlDbType.Int).Value = actorUserId;
        command.Parameters.Add("@Action", SqlDbType.NVarChar, 150).Value = action;
        command.Parameters.Add("@EntityId", SqlDbType.NVarChar, 100).Value = entityId.ToString();
        command.Parameters.Add("@Details", SqlDbType.NVarChar, 1000).Value = details;
        command.ExecuteNonQuery();
    }

    private static CategoryDataException Describe(Exception exception, string fallback)
    {
        if (exception is CategoryDataException data) return data;
        if (exception is SqlException sql)
        {
            return sql.Number switch
            {
                2601 or 2627 => new CategoryDataException("اسم التصنيف مسجل بالفعل. اختر اسمًا مختلفًا.", sql),
                547 => new CategoryDataException(
                    "التصنيف مرتبط بأصناف أو تصنيفات فرعية ولا يمكن حذفه. انقل الارتباطات أولًا.", sql),
                1205 => new CategoryDataException("تعارضت العملية مع مستخدم آخر. حدّث البيانات ثم أعد المحاولة.", sql),
                229 or 230 => new CategoryDataException(
                    "حساب الاتصال لا يملك صلاحية تعديل التصنيفات. شغّل sql/10_EnableCategoryAdmin.sql على الخادم ثم أعد المحاولة.",
                    sql),
                >= 50000 => new CategoryDataException(sql.Message, sql),
                _ => new CategoryDataException(fallback, sql)
            };
        }

        return new CategoryDataException(fallback, exception);
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
