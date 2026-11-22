using System.Data;
using Microsoft.Data.SqlClient;
using ProjectZ.Models;
using ProjectZ.Services;

namespace ProjectZ.Data;

public sealed record SecurityRoleRecord(int Id, string Name, string Description, bool IsSystem, int AssignedUsers);
public sealed record PermissionCatalogRecord(int Id, string Code, string Name, string Module);
public sealed record SecurityUserRecord(int Id, string Username, string DisplayName, int? RoleId);
public sealed record PermissionAdministrationData(
    IReadOnlyList<SecurityRoleRecord> Roles,
    IReadOnlyList<PermissionCatalogRecord> Permissions,
    IReadOnlyList<SecurityUserRecord> Users,
    IReadOnlySet<string> SelectedRolePermissions);

/// <summary>Persists roles, their permission matrix, and each user's single role assignment.</summary>
public sealed class PermissionsRepository(ConnectionSettingsProvider settings, UserSession actor)
{
    public PermissionAdministrationData Load(int? roleId = null)
    {
        DemandAdministrator();
        using var connection = OpenConnection();
        var roles = new List<SecurityRoleRecord>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
SELECT r.Id,r.Name,r.Description,r.IsSystem,COUNT(ur.UserId)
FROM dbo.Roles r LEFT JOIN dbo.UserRoles ur ON ur.RoleId=r.Id
GROUP BY r.Id,r.Name,r.Description,r.IsSystem ORDER BY r.IsSystem DESC,r.Name;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                roles.Add(new SecurityRoleRecord(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetInt32(4)));
        }

        var permissions = new List<PermissionCatalogRecord>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id,Code,Name,Module FROM dbo.Permissions ORDER BY Module,Name;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                permissions.Add(new PermissionCatalogRecord(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        var users = new List<SecurityUserRecord>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
SELECT u.Id,u.Username,u.DisplayName,ur.RoleId
FROM dbo.Users u LEFT JOIN dbo.UserRoles ur ON ur.UserId=u.Id
ORDER BY u.Username;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                users.Add(new SecurityUserRecord(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetInt32(3)));
        }

        var selectedPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (roleId is int selectedId)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT p.Code FROM dbo.RolePermissions rp
JOIN dbo.Permissions p ON p.Id=rp.PermissionId WHERE rp.RoleId=@RoleId;";
            command.Parameters.Add("@RoleId", SqlDbType.Int).Value = selectedId;
            using var reader = command.ExecuteReader();
            while (reader.Read()) selectedPermissions.Add(reader.GetString(0));
        }

        return new PermissionAdministrationData(roles, permissions, users, selectedPermissions);
    }

    public SecurityRoleRecord CreateRole(string name, string description, IReadOnlyCollection<string> permissionCodes)
    {
        DemandAdministrator();
        name = Normalize(name, 100, "أدخل اسم الدور (حتى 100 حرف).");
        description = NormalizeOptional(description, 500);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            using var command = new SqlCommand(@"
IF EXISTS(SELECT 1 FROM dbo.Roles WITH(UPDLOCK,HOLDLOCK) WHERE Name=@Name)
    THROW 50322,N'اسم الدور مستخدم بالفعل.',1;
INSERT dbo.Roles(Name,Description,IsSystem) OUTPUT inserted.Id VALUES(@Name,@Description,0);", connection, transaction);
            command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = name;
            command.Parameters.Add("@Description", SqlDbType.NVarChar, 500).Value = description;
            var id = Convert.ToInt32(command.ExecuteScalar());
            InsertRolePermissions(connection, transaction, id, permissionCodes);
            Audit(connection, transaction, "CreateRole", "Role", id, $"إنشاء دور {name}");
            transaction.Commit();
            return new SecurityRoleRecord(id, name, description, false, 0);
        }
        catch { transaction.Rollback(); throw; }
    }

    public SecurityRoleRecord SaveRole(int id, string name, string description, IReadOnlyCollection<string> permissionCodes)
    {
        DemandAdministrator();
        name = Normalize(name, 100, "أدخل اسم الدور (حتى 100 حرف).");
        description = NormalizeOptional(description, 500);
        var codes = permissionCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            bool isSystem;
            using (var check = new SqlCommand("SELECT IsSystem FROM dbo.Roles WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id;", connection, transaction))
            {
                check.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                var value = check.ExecuteScalar();
                if (value is null) throw new InvalidOperationException("الدور غير موجود؛ حدّث الشاشة ثم أعد المحاولة.");
                isSystem = Convert.ToBoolean(value);
            }
            using (var update = new SqlCommand(@"
IF EXISTS(SELECT 1 FROM dbo.Roles WHERE Name=@Name AND Id<>@Id)
    THROW 50322,N'اسم الدور مستخدم بالفعل.',1;
UPDATE dbo.Roles SET Name=@Name,Description=@Description WHERE Id=@Id;", connection, transaction))
            {
                update.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                update.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = isSystem ? GetRoleName(connection, transaction, id) : name;
                update.Parameters.Add("@Description", SqlDbType.NVarChar, 500).Value = description;
                update.ExecuteNonQuery();
            }
            using (var clear = new SqlCommand("DELETE dbo.RolePermissions WHERE RoleId=@Id;", connection, transaction))
            {
                clear.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                clear.ExecuteNonQuery();
            }
            InsertRolePermissions(connection, transaction, id, codes);
            int assigned;
            using (var count = new SqlCommand("SELECT COUNT(*) FROM dbo.UserRoles WHERE RoleId=@Id;", connection, transaction))
            {
                count.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                assigned = Convert.ToInt32(count.ExecuteScalar());
            }
            var savedName = isSystem ? GetRoleName(connection, transaction, id) : name;
            Audit(connection, transaction, "UpdateRolePermissions", "Role", id, $"تحديث صلاحيات الدور {savedName}");
            transaction.Commit();
            return new SecurityRoleRecord(id, savedName, description, isSystem, assigned);
        }
        catch { transaction.Rollback(); throw; }
    }

    public void AssignRole(int userId, int? roleId)
    {
        DemandAdministrator();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            using (var verifyUser = new SqlCommand("SELECT Username FROM dbo.Users WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id;", connection, transaction))
            {
                verifyUser.Parameters.Add("@Id", SqlDbType.Int).Value = userId;
                var username = Convert.ToString(verifyUser.ExecuteScalar());
                if (username is null) throw new InvalidOperationException("المستخدم غير موجود؛ حدّث الشاشة.");
                if (username.Equals("admin", StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("لا يمكن تغيير دور حساب admin؛ صلاحياته الكاملة ثابتة.");
            }
            if (roleId is int selected)
            {
                using var verify = new SqlCommand("SELECT COUNT(*) FROM dbo.Roles WHERE Id=@Id;", connection, transaction);
                verify.Parameters.Add("@Id", SqlDbType.Int).Value = selected;
                if (Convert.ToInt32(verify.ExecuteScalar()) != 1)
                    throw new InvalidOperationException("الدور المحدد غير موجود؛ حدّث الشاشة.");
            }
            using (var remove = new SqlCommand("DELETE dbo.UserRoles WHERE UserId=@UserId;", connection, transaction))
            {
                remove.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                remove.ExecuteNonQuery();
            }
            if (roleId is int role)
            {
                using var add = new SqlCommand("INSERT dbo.UserRoles(UserId,RoleId,AssignedByUserId) VALUES(@UserId,@RoleId,@ActorId);", connection, transaction);
                add.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                add.Parameters.Add("@RoleId", SqlDbType.Int).Value = role;
                add.Parameters.Add("@ActorId", SqlDbType.Int).Value = actor.Id;
                add.ExecuteNonQuery();
            }
            Audit(connection, transaction, "AssignUserRole", "User", userId,
                roleId is int assignedRole ? $"إسناد الدور {assignedRole} للمستخدم" : "إزالة دور المستخدم");
            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
    }

    public void DeleteRole(int roleId)
    {
        DemandAdministrator();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            using (var check = new SqlCommand("SELECT IsSystem,(SELECT COUNT(*) FROM dbo.UserRoles WHERE RoleId=r.Id) FROM dbo.Roles r WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id;", connection, transaction))
            {
                check.Parameters.Add("@Id", SqlDbType.Int).Value = roleId;
                using var reader = check.ExecuteReader();
                if (!reader.Read()) throw new InvalidOperationException("الدور غير موجود.");
                if (reader.GetBoolean(0)) throw new InvalidOperationException("لا يمكن حذف دور نظام أساسي.");
                if (reader.GetInt32(1) != 0) throw new InvalidOperationException("لا يمكن حذف دور مُسند لمستخدم؛ انقل المستخدمين إلى دور آخر أولًا.");
            }
            var roleName = GetRoleName(connection, transaction, roleId);
            using (var clear = new SqlCommand("DELETE dbo.RolePermissions WHERE RoleId=@Id; DELETE dbo.Roles WHERE Id=@Id;", connection, transaction))
            {
                clear.Parameters.Add("@Id", SqlDbType.Int).Value = roleId;
                clear.ExecuteNonQuery();
            }
            Audit(connection, transaction, "DeleteRole", "Role", roleId, $"حذف دور {roleName}");
            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
    }

    private void InsertRolePermissions(SqlConnection connection, SqlTransaction transaction, int roleId,
        IEnumerable<string> permissionCodes)
    {
        foreach (var code in permissionCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var add = new SqlCommand(@"
INSERT dbo.RolePermissions(RoleId,PermissionId)
SELECT @Id,Id FROM dbo.Permissions WHERE Code=@Code;", connection, transaction);
            add.Parameters.Add("@Id", SqlDbType.Int).Value = roleId;
            add.Parameters.Add("@Code", SqlDbType.NVarChar, 100).Value = code;
            if (add.ExecuteNonQuery() != 1)
                throw new InvalidOperationException($"رمز صلاحية غير معروف: {code}");
        }
    }

    private void Audit(SqlConnection connection, SqlTransaction transaction, string action, string entityType,
        int entityId, string detail)
    {
        using var command = new SqlCommand(@"
INSERT dbo.AuditLog(ActorUserId,Module,ActionName,EntityType,EntityId,NewValuesJson)
VALUES(@Actor,N'Permissions',@Action,@EntityType,@EntityId,@Details);", connection, transaction);
        command.Parameters.Add("@Actor", SqlDbType.Int).Value = actor.Id;
        command.Parameters.Add("@Action", SqlDbType.NVarChar, 150).Value = action;
        command.Parameters.Add("@EntityType", SqlDbType.NVarChar, 100).Value = entityType;
        command.Parameters.Add("@EntityId", SqlDbType.NVarChar, 100).Value = entityId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        command.Parameters.Add("@Details", SqlDbType.NVarChar, -1).Value = System.Text.Json.JsonSerializer.Serialize(new { detail });
        command.ExecuteNonQuery();
    }

    private void DemandAdministrator()
    {
        if (!actor.HasPermission("SECURITY.MANAGE"))
            throw new UnauthorizedAccessException("ليس لديك صلاحية إدارة الأدوار والصلاحيات.");
    }

    private SqlConnection OpenConnection()
    {
        var connection = new SqlConnection(settings.GetConnectionString());
        connection.Open();
        return connection;
    }

    private static string GetRoleName(SqlConnection connection, SqlTransaction? transaction, int id)
    {
        using var command = new SqlCommand("SELECT Name FROM dbo.Roles WHERE Id=@Id;", connection, transaction);
        command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        return Convert.ToString(command.ExecuteScalar()) ?? "";
    }

    private static string Normalize(string value, int max, string error)
    {
        var result = (value ?? "").Trim();
        if (result.Length == 0 || result.Length > max) throw new ArgumentException(error);
        return result;
    }

    private static string NormalizeOptional(string value, int max)
    {
        var result = (value ?? "").Trim();
        if (result.Length > max) throw new ArgumentException($"النص يتجاوز الحد الأقصى ({max} حرفًا).");
        return result;
    }
}
