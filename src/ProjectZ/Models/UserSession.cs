namespace ProjectZ.Models;

/// <summary>Authenticated application identity and its permission snapshot for this login session.</summary>
public sealed record UserSession(
    int Id,
    string Username,
    string DisplayName,
    IReadOnlySet<string>? GrantedPermissions = null)
{
    public bool IsSystemAdministrator => string.Equals(Username, "admin", StringComparison.OrdinalIgnoreCase);

    public bool HasPermission(string code) => IsSystemAdministrator
        || GrantedPermissions is null // Null is reserved for in-memory design/test sessions; login always supplies a snapshot.
        || GrantedPermissions.Contains(code);

    public void Demand(string code)
    {
        if (!HasPermission(code))
            throw new UnauthorizedAccessException($"ليس لديك صلاحية تنفيذ هذا الإجراء ({code}).");
    }
}
