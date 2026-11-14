using System.Data;
using ProjectZ.Models;
using ProjectZ.Services;
using Microsoft.Data.SqlClient;

namespace ProjectZ.Data;

public sealed record UserCredentials(UserSession User, string PasswordHash, bool IsActive);

/// <summary>User-facing failure raised by the users layer; the message is safe to show in the UI.</summary>
public sealed class UserDataException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

/// <summary>A login account row as stored in dbo.Users. The password hash never leaves this file.</summary>
public sealed class UserRecord
{
    public int Id { get; init; }
    public string Username { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public bool IsActive { get; init; } = true;

    /// <summary>Last change to the login row, in UTC (no column other than CreatedAt/UpdatedAt exists here).</summary>
    public DateTime UpdatedAtUtc { get; init; }

    /// <summary>Linked business records (invoices, stock moves, audit lines...) that keep the row referenced.</summary>
    public int LinkedRecordsCount { get; init; }
}

/// <summary>
/// SQL Server persistence for login accounts: sign-in lookup plus the users screen.
/// Passwords are hashed with PBKDF2-HMAC-SHA256 (600,000 iterations) before the row is written;
/// the plain password is never stored, logged or put in a parameter.
/// </summary>
public sealed class UserRepository(ConnectionSettingsProvider settings, int actorUserId = 0)
{
    private const int MaxUsernameLength = 100;
    private const int MaxDisplayNameLength = 150;
    private const int MinPasswordLength = 6;
    private const int MaxPasswordLength = 1024;

    // ------------------------------------------------------------ sign in

    public async Task<UserCredentials?> FindAsync(string username, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(settings.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT Id, Username, DisplayName, PasswordHash, IsActive FROM dbo.Users WHERE Username = @Username;", connection)
            { CommandTimeout = 15 };
        command.Parameters.Add(new SqlParameter("@Username", SqlDbType.NVarChar, 100) { Value = username });
        await using SqlDataReader reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var dbUsername = reader.GetString(1);
        var displayName = reader.IsDBNull(2) || string.IsNullOrWhiteSpace(reader.GetString(2))
            ? dbUsername
            : reader.GetString(2);
        return new UserCredentials(new UserSession(reader.GetInt32(0), dbUsername, displayName),
            reader.GetString(3), reader.GetBoolean(4));
    }

    // -------------------------------------------------------------- read side

    /// <summary>
    /// Loads every login account with a count of the business records that reference it,
    /// so the screen can explain in advance why a delete would be refused.
    /// </summary>
    public List<UserRecord> LoadAll()
    {
        var rows = new List<UserRecord>();
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT u.Id, u.Username, u.DisplayName, u.IsActive, u.CreatedAt,
       (SELECT COUNT(*) FROM dbo.SalesInvoices r WHERE r.CashierUserId = u.Id)
     + (SELECT COUNT(*) FROM dbo.PurchaseInvoices p WHERE p.BuyerUserId = u.Id)
     + (SELECT COUNT(*) FROM dbo.StockMovements s WHERE s.ActorUserId = u.Id)
     + (SELECT COUNT(*) FROM dbo.FinancialTransactions f WHERE f.ActorUserId = u.Id)
     + (SELECT COUNT(*) FROM dbo.MaintenanceOrders m WHERE m.ReceivedByUserId = u.Id)
     + (SELECT COUNT(*) FROM dbo.CustomerTransfers t WHERE t.ActorUserId = u.Id)
     + (SELECT COUNT(*) FROM dbo.AuditLog a WHERE a.ActorUserId = u.Id)
FROM dbo.Users u
ORDER BY u.Username;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new UserRecord
                {
                    Id = reader.GetInt32(0),
                    Username = reader.GetString(1),
                    DisplayName = reader.GetString(2),
                    IsActive = reader.GetBoolean(3),
                    UpdatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc),
                    LinkedRecordsCount = reader.GetInt32(5)
                });
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not UserDataException))
        {
            throw Describe(ex, "تعذر تحميل المستخدمين من قاعدة البيانات.");
        }

        return rows;
    }

    // ------------------------------------------------------------- write side

    /// <summary>Creates a login account. The password is hashed here before the INSERT.</summary>
    public UserRecord Create(string username, string displayName, string password, bool isActive)
    {
        var login = NormalizeUsername(username);
        var name = NormalizeDisplayName(displayName, login);
        var secret = NormalizePassword(password);
        var hash = PasswordHasher.Hash(secret);
        secret = "";

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                EnsureUsernameIsFree(connection, transaction, login, exceptId: null);

                int id;
                using (var command = CreateCommand(connection, transaction, @"
INSERT dbo.Users(Username,PasswordHash,DisplayName,IsActive)
OUTPUT inserted.Id
VALUES(@Username,@PasswordHash,@DisplayName,@IsActive);"))
                {
                    command.Parameters.Add("@Username", SqlDbType.NVarChar, MaxUsernameLength).Value = login;
                    command.Parameters.Add("@PasswordHash", SqlDbType.NVarChar, 500).Value = hash;
                    command.Parameters.Add("@DisplayName", SqlDbType.NVarChar, MaxDisplayNameLength).Value = name;
                    command.Parameters.Add("@IsActive", SqlDbType.Bit).Value = isActive;
                    id = (int)command.ExecuteScalar()!;
                }

                Audit(connection, transaction, "CreateUser", id, $"إضافة مستخدم {login}");
                transaction.Commit();
                return new UserRecord { Id = id, Username = login, DisplayName = name, IsActive = isActive };
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not UserDataException))
        {
            throw Describe(ex, "تعذر حفظ المستخدم في قاعدة البيانات.");
        }
    }

    /// <summary>
    /// Updates the profile of a login account (username, display name, activation).
    /// The password hash is left untouched; use <see cref="ResetPassword"/> for that.
    /// </summary>
    public UserRecord Update(int id, string username, string displayName, bool isActive)
    {
        var login = NormalizeUsername(username);
        var name = NormalizeDisplayName(displayName, login);

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var current = LoadOne(connection, transaction, id);
                EnsureUsernameIsFree(connection, transaction, login, exceptId: id);

                if (current.IsActive && !isActive)
                {
                    if (id == actorUserId)
                        throw new UserDataException("لا يمكنك تعطيل الحساب الذي تستخدمه الآن. اطلب من مستخدم آخر تعطيله.");

                    if (CountOtherActiveAccounts(connection, transaction, exceptId: id) == 0)
                        throw new UserDataException(
                            "لا يمكن تعطيل هذا الحساب لأنه آخر حساب نشط يمكنه الدخول. أضف حسابًا نشطًا آخر أولًا.");
                }

                using (var command = CreateCommand(connection, transaction, @"
UPDATE dbo.Users SET Username=@Username, DisplayName=@DisplayName, IsActive=@IsActive
WHERE Id=@Id;"))
                {
                    command.Parameters.Add("@Username", SqlDbType.NVarChar, MaxUsernameLength).Value = login;
                    command.Parameters.Add("@DisplayName", SqlDbType.NVarChar, MaxDisplayNameLength).Value = name;
                    command.Parameters.Add("@IsActive", SqlDbType.Bit).Value = isActive;
                    command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                    if (command.ExecuteNonQuery() != 1)
                        throw new UserDataException("المستخدم غير موجود في قاعدة البيانات؛ حدّث الشاشة وأعد المحاولة.");
                }

                Audit(connection, transaction, "UpdateUser", id,
                    $"تعديل المستخدم من {current.Username} إلى {login}"
                    + (current.IsActive == isActive ? "" : isActive ? " (تنشيط)" : " (تعطيل)"));
                transaction.Commit();
                return new UserRecord
                {
                    Id = id,
                    Username = login,
                    DisplayName = name,
                    IsActive = isActive,
                    UpdatedAtUtc = DateTime.UtcNow,
                    LinkedRecordsCount = current.LinkedRecordsCount
                };
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not UserDataException))
        {
            throw Describe(ex, "تعذر تعديل المستخدم في قاعدة البيانات.");
        }
    }

    /// <summary>Stores a new password hash for an account. The password itself is never written.</summary>
    public void ResetPassword(int id, string password)
    {
        var secret = NormalizePassword(password);
        var hash = PasswordHasher.Hash(secret);
        secret = "";

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            try
            {
                var current = LoadOne(connection, transaction, id);
                using (var command = CreateCommand(connection, transaction,
                    "UPDATE dbo.Users SET PasswordHash=@PasswordHash WHERE Id=@Id;"))
                {
                    command.Parameters.Add("@PasswordHash", SqlDbType.NVarChar, 500).Value = hash;
                    command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                    if (command.ExecuteNonQuery() != 1)
                        throw new UserDataException("المستخدم غير موجود في قاعدة البيانات؛ حدّث الشاشة وأعد المحاولة.");
                }

                Audit(connection, transaction, "ResetUserPassword", id, $"إعادة تعيين كلمة مرور {current.Username}");
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not UserDataException))
        {
            throw Describe(ex, "تعذر تغيير كلمة المرور في قاعدة البيانات.");
        }
    }

    /// <summary>
    /// Deletes a login account that never appeared in business records.
    /// Any linked invoice, stock move, transfer or audit line keeps the row: the screen disables the
    /// button and the database refuses as a second guard, so history never loses its user.
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

                if (id == actorUserId)
                    throw new UserDataException("لا يمكنك حذف الحساب الذي تستخدمه الآن. سجّل الدخول بحساب آخر لحذفه.");

                if (current.LinkedRecordsCount > 0)
                    throw new UserDataException(
                        $"لا يمكن حذف المستخدم «{current.Username}» لأنه مرتبط بـ {current.LinkedRecordsCount} سجل " +
                        "(فواتير أو حركات مخزون أو تحويلات أو سجل تدقيق). عطّل الحساب بدلًا من حذفه ليبقى السجل سليمًا.");

                if (CountOtherActiveAccounts(connection, transaction, exceptId: id) == 0)
                    throw new UserDataException(
                        "لا يمكن حذف هذا المستخدم لأنه آخر حساب نشط يمكنه الدخول. أضف حسابًا آخر أولًا.");

                // The role link must go first: dbo.UserRoles has a foreign key to this row.
                DeleteRoleLinks(connection, transaction, id);

                using (var command = CreateCommand(connection, transaction, "DELETE dbo.Users WHERE Id=@Id;"))
                {
                    command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                    if (command.ExecuteNonQuery() != 1)
                        throw new UserDataException("المستخدم غير موجود في قاعدة البيانات؛ حدّث الشاشة وأعد المحاولة.");
                }

                Audit(connection, transaction, "DeleteUser", id, $"حذف مستخدم {current.Username}");
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex) when (ex is SqlException or (InvalidOperationException and not UserDataException))
        {
            throw Describe(ex, "تعذر حذف المستخدم من قاعدة البيانات.");
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// dbo.UserRoles exists in the schema but this screen does not use roles. Clearing the link first
    /// keeps that foreign key valid when such a row happens to exist; a missing table is ignored.
    /// </summary>
    private static void DeleteRoleLinks(SqlConnection connection, SqlTransaction transaction, int id)
    {
        using var exists = CreateCommand(connection, transaction,
            "SELECT CASE WHEN OBJECT_ID(N'dbo.UserRoles', N'U') IS NULL THEN 0 ELSE 1 END;");
        if (Convert.ToInt32(exists.ExecuteScalar()!) == 0) return;

        using var cleanup = CreateCommand(connection, transaction, "DELETE dbo.UserRoles WHERE UserId=@Id;");
        cleanup.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        cleanup.ExecuteNonQuery();
    }

    /// <summary>At least one active account must survive, otherwise nobody could sign in again.</summary>
    private static int CountOtherActiveAccounts(SqlConnection connection, SqlTransaction transaction, int exceptId)
    {
        using var command = CreateCommand(connection, transaction, @"
SELECT COUNT(*) FROM dbo.Users WHERE IsActive = 1 AND Id <> @Id;");
        command.Parameters.Add("@Id", SqlDbType.Int).Value = exceptId;
        return Convert.ToInt32(command.ExecuteScalar()!);
    }

    private static UserRecord LoadOne(SqlConnection connection, SqlTransaction transaction, int id)
    {
        using var command = CreateCommand(connection, transaction, @"
SELECT u.Id, u.Username, u.DisplayName, u.IsActive, u.CreatedAt,
       (SELECT COUNT(*) FROM dbo.SalesInvoices r WHERE r.CashierUserId = u.Id)
     + (SELECT COUNT(*) FROM dbo.PurchaseInvoices p WHERE p.BuyerUserId = u.Id)
     + (SELECT COUNT(*) FROM dbo.StockMovements s WHERE s.ActorUserId = u.Id)
     + (SELECT COUNT(*) FROM dbo.FinancialTransactions f WHERE f.ActorUserId = u.Id)
     + (SELECT COUNT(*) FROM dbo.MaintenanceOrders m WHERE m.ReceivedByUserId = u.Id)
     + (SELECT COUNT(*) FROM dbo.CustomerTransfers t WHERE t.ActorUserId = u.Id)
     + (SELECT COUNT(*) FROM dbo.AuditLog a WHERE a.ActorUserId = u.Id)
FROM dbo.Users u
WHERE u.Id = @Id;");
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new UserDataException("المستخدم غير موجود في قاعدة البيانات؛ حدّث الشاشة وأعد المحاولة.");

        return new UserRecord
        {
            Id = reader.GetInt32(0),
            Username = reader.GetString(1),
            DisplayName = reader.GetString(2),
            IsActive = reader.GetBoolean(3),
            UpdatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc),
            LinkedRecordsCount = reader.GetInt32(5)
        };
    }

    /// <summary>SQL Server compares NVARCHAR case-insensitively by default, so «Admin» and «admin» clash here too.</summary>
    private static void EnsureUsernameIsFree(SqlConnection connection, SqlTransaction transaction, string username, int? exceptId)
    {
        using var command = CreateCommand(connection, transaction, @"
SELECT TOP 1 Username FROM dbo.Users WITH (UPDLOCK, HOLDLOCK)
WHERE Username = @Username AND (@ExceptId IS NULL OR Id <> @ExceptId);");
        command.Parameters.Add("@Username", SqlDbType.NVarChar, MaxUsernameLength).Value = username;
        command.Parameters.Add("@ExceptId", SqlDbType.Int).Value = (object?)exceptId ?? DBNull.Value;

        if (command.ExecuteScalar() is string existing)
            throw new UserDataException($"اسم المستخدم «{existing}» مسجل بالفعل. اختر اسم دخول مختلفًا.");
    }

    private static string NormalizeUsername(string username)
    {
        var trimmed = (username ?? "").Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new UserDataException("يرجى إدخال اسم المستخدم أولًا.");
        if (trimmed.Length > MaxUsernameLength)
            throw new UserDataException($"اسم المستخدم يجب ألا يتجاوز {MaxUsernameLength} حرفًا.");
        if (trimmed.Any(char.IsWhiteSpace))
            throw new UserDataException("اسم المستخدم لا يجب أن يحتوي على مسافات.");
        return trimmed;
    }

    private static string NormalizeDisplayName(string displayName, string username)
    {
        var trimmed = (displayName ?? "").Trim();
        if (trimmed.Length == 0) return username;
        if (trimmed.Length > MaxDisplayNameLength)
            throw new UserDataException($"الاسم الظاهر يجب ألا يتجاوز {MaxDisplayNameLength} حرفًا.");
        return trimmed;
    }

    private static string NormalizePassword(string password)
    {
        var value = password ?? "";
        if (value.Length == 0)
            throw new UserDataException("يرجى إدخال كلمة المرور.");
        if (value.Length < MinPasswordLength)
            throw new UserDataException($"كلمة المرور يجب أن تكون {MinPasswordLength} أحرف على الأقل.");
        if (value.Length > MaxPasswordLength)
            throw new UserDataException($"كلمة المرور يجب ألا تتجاوز {MaxPasswordLength} حرفًا.");
        return value;
    }

    private void Audit(SqlConnection connection, SqlTransaction transaction, string action, int entityId, string details)
    {
        using var command = CreateCommand(connection, transaction, @"
INSERT dbo.AuditLog(ActorUserId,Module,ActionName,EntityType,EntityId,NewValuesJson)
VALUES(@Actor,N'Users',@Action,N'User',@EntityId,JSON_MODIFY(N'{}',N'$.details',@Details));");
        object actor = actorUserId > 0 ? actorUserId : DBNull.Value;
        command.Parameters.Add("@Actor", SqlDbType.Int).Value = actor;
        command.Parameters.Add("@Action", SqlDbType.NVarChar, 150).Value = action;
        command.Parameters.Add("@EntityId", SqlDbType.NVarChar, 100).Value = entityId.ToString();
        command.Parameters.Add("@Details", SqlDbType.NVarChar, 1000).Value = details;
        command.ExecuteNonQuery();
    }

    private static UserDataException Describe(Exception exception, string fallback)
    {
        if (exception is UserDataException data) return data;
        if (exception is SqlException sql)
        {
            return sql.Number switch
            {
                2601 or 2627 => new UserDataException("اسم المستخدم مسجل بالفعل. اختر اسم دخول مختلفًا.", sql),
                547 => new UserDataException(
                    "المستخدم مرتبط بسجلات في النظام ولا يمكن حذفه. عطّل الحساب بدلًا من حذفه.", sql),
                1205 => new UserDataException("تعارضت العملية مع مستخدم آخر. حدّث البيانات ثم أعد المحاولة.", sql),
                229 or 230 => new UserDataException(
                    "حساب الاتصال لا يملك صلاحية إدارة المستخدمين. شغّل sql/14_EnableUserAdmin.sql على الخادم ثم أعد المحاولة.",
                    sql),
                >= 50000 => new UserDataException(sql.Message, sql),
                _ => new UserDataException(fallback, sql)
            };
        }

        return new UserDataException(fallback, exception);
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
