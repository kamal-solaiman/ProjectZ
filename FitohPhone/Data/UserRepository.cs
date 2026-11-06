using Microsoft.Data.SqlClient;
using FitohPhone.Models;

namespace FitohPhone.Data;

/// <summary>
/// Direct database access for the Users table using SqlConnection / SqlCommand /
/// SqlParameter / SqlDataReader only. No ORM, no API, no intermediary.
/// </summary>
public sealed class UserRepository
{
    private const string SelectByUsernameSql = @"
        SELECT Id, Username, PasswordHash, DisplayName, IsActive, CreatedAt
        FROM dbo.Users
        WHERE Username = @Username;";

    /// <summary>Returns the user with the given username, or null if not found.</summary>
    public User? GetByUsername(string username)
    {
        using SqlConnection connection = DatabaseConnectionFactory.CreateConnection();
        using SqlCommand command = new SqlCommand(SelectByUsernameSql, connection);
        command.Parameters.Add(new SqlParameter("@Username", System.Data.SqlDbType.NVarChar, 100) { Value = username });

        connection.Open();
        using SqlDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return new User
        {
            Id = reader.GetInt32(0),
            Username = reader.GetString(1),
            PasswordHash = reader.GetString(2),
            DisplayName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
            IsActive = reader.GetBoolean(4),
            CreatedAt = reader.GetDateTime(5)
        };
    }
}
