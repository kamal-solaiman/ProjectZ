using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

internal static class SqlTest
{
    internal static IEnumerable<string> Batches(string script) => Regex.Split(script, @"(?m)^\s*GO\s*(?:--.*)?$", RegexOptions.IgnoreCase)
        .Where(batch => !string.IsNullOrWhiteSpace(batch));

    internal static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine("PASS: " + name);
    }

    internal static void Execute(SqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandTimeout = 120;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    internal static T Scalar<T>(SqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandTimeout = 120;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T));
    }
}
