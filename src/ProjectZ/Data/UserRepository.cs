using System.Data;
using ProjectZ.Models;
using ProjectZ.Services;
using Microsoft.Data.SqlClient;

namespace ProjectZ.Data;

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
}
