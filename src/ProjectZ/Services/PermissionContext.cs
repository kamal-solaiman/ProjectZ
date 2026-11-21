using ProjectZ.Models;

namespace ProjectZ.Services;

/// <summary>
/// Current authenticated permission snapshot. UI visibility is convenience only; every protected
/// repository mutation calls Demand so invoking a command through another screen cannot bypass it.
/// Direct SQL use of the shared ProjectZClient login is explicitly outside this application's policy.
/// </summary>
public static class PermissionContext
{
    private static UserSession? current;
    public static UserSession? Current => Volatile.Read(ref current);

    public static void Establish(UserSession session) => Volatile.Write(ref current, session);
    public static void Clear() => Volatile.Write(ref current, null);

    public static void Demand(string code)
    {
        var session = Current ?? throw new UnauthorizedAccessException("لا توجد جلسة مستخدم موثقة.");
        session.Demand(code);
    }

    public static void DemandAny(params string[] codes)
    {
        var session = Current ?? throw new UnauthorizedAccessException("لا توجد جلسة مستخدم موثقة.");
        if (!codes.Any(session.HasPermission))
            throw new UnauthorizedAccessException("ليس لديك صلاحية تنفيذ هذا الإجراء.");
    }
}
