namespace Chat.Domain.DTOs.Permissions;

public sealed class PermissionRoleDto
{
    public int RoleId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
}

public sealed class PermissionTabDto
{
    public int PermissionId { get; set; }
    public int TabId { get; set; }
    public string TabCode { get; set; } = string.Empty;
    public string TabName { get; set; } = string.Empty;
    public string? RoutePath { get; set; }
    public string? IconClass { get; set; }
    public string? SectionCode { get; set; }
    public int? ParentTabId { get; set; }
    public int SortOrder { get; set; }
    public bool CanView { get; set; }
    public bool CanAdd { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool IsActive { get; set; }
    public int RoleId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
}

public sealed class PermissionMatrixSaveItem
{
    public int? PermissionId { get; set; }
    public int TabId { get; set; }
    public bool CanView { get; set; }
    public bool CanAdd { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool IsActive { get; set; }
}

public sealed class PermissionMatrixSaveRequest
{
    public int RoleId { get; set; }
    public string? RoleCode { get; set; }
    public List<PermissionMatrixSaveItem> Rows { get; set; } = new();
}

public static class PermissionRoleNames
{
    public static string Friendly(string? roleCode)
    {
        var code = (roleCode ?? "").Trim().ToUpperInvariant();
        if (code.EndsWith("_ADMIN", StringComparison.Ordinal) || code == "ADMIN")
            return "Admin";
        if (code.Contains("EMPLOYEE", StringComparison.Ordinal) || code.EndsWith("_USER", StringComparison.Ordinal))
            return "Employee";
        return string.IsNullOrWhiteSpace(roleCode) ? "Role" : roleCode!;
    }

    /// <summary>CHAT section vs MASTER section for sidebar grouping.</summary>
    public static string? ResolveSectionCode(string? menuCode, int? parentMenuId)
    {
        var code = (menuCode ?? "").Trim().ToUpperInvariant();
        if (code is "CHAT_MASTER" || parentMenuId == 7 || code.StartsWith("CHAT_MANAGE_", StringComparison.Ordinal))
            return "MASTER";
        if (code is "CHAT_LOGOUT")
            return null;
        return "CHAT";
    }
}
