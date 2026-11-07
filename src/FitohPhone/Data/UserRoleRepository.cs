using System.Data;
using FitohPhone.Models;
using FitohPhone.Services;
using Microsoft.Data.SqlClient;

namespace FitohPhone.Data;

public sealed class UserRoleRepository(ConnectionSettingsProvider settings)
{
    public async Task<IReadOnlyList<UserRole>> GetAllAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT Id, RoleName, RoleNameAr, Description, IsActive, CreatedAt FROM dbo.UserRoles ORDER BY RoleNameAr", conn) { CommandTimeout = 15 };
        var list = new List<UserRole>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new UserRole(
                r.GetInt32(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3), r.GetBoolean(4), r.GetDateTime(5)));
        return list;
    }

    public async Task<IReadOnlyList<UserPermission>> GetPermissionsForUserAsync(int userId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT Id, UserId, Module, ModuleNameAr, CanView, CanAdd, CanEdit, CanDelete,
                     CanPrint, IsActive, CreatedAt
              FROM dbo.UserPermissions WHERE UserId=@UserId", conn) { CommandTimeout = 15 };
        cmd.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = userId });
        var list = new List<UserPermission>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new UserPermission(
                r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3),
                r.GetBoolean(4), r.GetBoolean(5), r.GetBoolean(6), r.GetBoolean(7),
                r.GetBoolean(8), r.GetBoolean(9), r.GetDateTime(10)));
        return list;
    }

    public async Task<bool> SavePermissionsAsync(int userId, IEnumerable<UserPermission> permissions, CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        SqlTransaction tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using var del = new SqlCommand("DELETE FROM dbo.UserPermissions WHERE UserId=@UserId", conn, tx) { CommandTimeout = 15 };
            del.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = userId });
            await del.ExecuteNonQueryAsync(ct);

            foreach (var p in permissions)
            {
                await using var ins = new SqlCommand(
                    @"INSERT INTO dbo.UserPermissions (UserId, Module, ModuleNameAr, CanView, CanAdd, CanEdit, CanDelete, CanPrint, IsActive, CreatedAt)
                     VALUES (@UserId, @Module, @ModuleNameAr, @CanView, @CanAdd, @CanEdit, @CanDelete, @CanPrint, 1, SYSUTCDATETIME())", conn, tx) { CommandTimeout = 15 };
                ins.Parameters.AddRange(new[]
                {
                    new SqlParameter("@UserId", SqlDbType.Int) { Value = userId },
                    new SqlParameter("@Module", SqlDbType.NVarChar, 50) { Value = p.Module },
                    new SqlParameter("@ModuleNameAr", SqlDbType.NVarChar, 100) { Value = p.ModuleNameAr },
                    new SqlParameter("@CanView", SqlDbType.Bit) { Value = p.CanView },
                    new SqlParameter("@CanAdd", SqlDbType.Bit) { Value = p.CanAdd },
                    new SqlParameter("@CanEdit", SqlDbType.Bit) { Value = p.CanEdit },
                    new SqlParameter("@CanDelete", SqlDbType.Bit) { Value = p.CanDelete },
                    new SqlParameter("@CanPrint", SqlDbType.Bit) { Value = p.CanPrint },
                });
                await ins.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return true;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
