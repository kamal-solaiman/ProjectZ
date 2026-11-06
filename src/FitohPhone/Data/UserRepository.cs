using System.Data;
using System.Collections.Generic;
using FitohPhone.Models;
using FitohPhone.Services;
using Microsoft.Data.SqlClient;

namespace FitohPhone.Data;

public sealed record UserCredentials(UserSession User, string PasswordHash, bool IsActive);

public sealed class UserRepository(ConnectionSettingsProvider settings)
{
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

    public async Task<IReadOnlyList<UserSession>> GetAllUsersAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(settings.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT Id, Username, DisplayName FROM dbo.Users ORDER BY DisplayName;", connection)
        { CommandTimeout = 15 };
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var list = new List<UserSession>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt32(0);
            var username = reader.GetString(1);
            var displayName = reader.IsDBNull(2) || string.IsNullOrWhiteSpace(reader.GetString(2))
                ? username
                : reader.GetString(2);
            list.Add(new UserSession(id, username, displayName));
        }
        return list;
    }
}
