using System.Data;
using FitohPhone.Services;
using Microsoft.Data.SqlClient;

namespace FitohPhone.Data;

public sealed class AuditLogRepository(ConnectionSettingsProvider settings)
{
    public async Task WriteAsync(int? userId, string action, string actionAr, string tableName,
        int? recordId, string? oldValues, string? newValues, string? ipAddress, string? notes,
        CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"INSERT INTO dbo.AuditLog (UserId, Action, ActionAr, TableName, RecordId,
                     OldValues, NewValues, IPAddress, Notes, EventDate)
              VALUES (@UserId, @Action, @ActionAr, @TableName, @RecordId,
                     @OldValues, @NewValues, @IPAddress, @Notes, SYSUTCDATETIME())", conn) { CommandTimeout = 15 };
        cmd.Parameters.AddRange(new[]
        {
            new SqlParameter("@UserId", SqlDbType.Int) { Value = (object)userId ?? DBNull.Value },
            new SqlParameter("@Action", SqlDbType.NVarChar, 50) { Value = action },
            new SqlParameter("@ActionAr", SqlDbType.NVarChar, 100) { Value = actionAr },
            new SqlParameter("@TableName", SqlDbType.NVarChar, 100) { Value = tableName },
            new SqlParameter("@RecordId", SqlDbType.Int) { Value = (object)recordId ?? DBNull.Value },
            new SqlParameter("@OldValues", SqlDbType.NVarChar, -1) { Value = (object)oldValues ?? DBNull.Value },
            new SqlParameter("@NewValues", SqlDbType.NVarChar, -1) { Value = (object)newValues ?? DBNull.Value },
            new SqlParameter("@IPAddress", SqlDbType.NVarChar, 45) { Value = (object)ipAddress ?? DBNull.Value },
            new SqlParameter("@Notes", SqlDbType.NVarChar, 1000) { Value = (object)notes ?? DBNull.Value },
        });
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
