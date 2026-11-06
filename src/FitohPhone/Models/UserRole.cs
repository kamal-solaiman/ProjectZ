namespace FitohPhone.Models;

public sealed record UserRole(
    int Id,
    string RoleName,
    string RoleNameAr,
    string? Description,
    bool IsActive,
    DateTime CreatedAt);

public sealed record UserPermission(
    int Id,
    int UserId,
    string Module,
    string ModuleNameAr,
    bool CanView,
    bool CanAdd,
    bool CanEdit,
    bool CanDelete,
    bool CanPrint,
    bool IsActive,
    DateTime CreatedAt);
