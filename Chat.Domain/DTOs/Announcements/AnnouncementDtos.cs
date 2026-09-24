namespace Chat.Domain.DTOs.Announcements;

public sealed class AnnouncementDto
{
    public long AnnouncementId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public int? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public string? Location { get; set; }
    public bool IsImportant { get; set; }
    public bool IsPinned { get; set; }
    public int StatusId { get; set; }
    public string? StatusName { get; set; }
    public long PostedBy { get; set; }
    public string? PostedByName { get; set; }
    public DateTimeOffset PostedDate { get; set; }
    public DateTimeOffset? ExpiryDate { get; set; }
    public int ReadCount { get; set; }
    public int RecipientCount { get; set; }
    public bool IsReadByCurrentUser { get; set; }
    public IReadOnlyList<long> RecipientUserIds { get; set; } = Array.Empty<long>();
}

public sealed class AnnouncementListResult
{
    public IReadOnlyList<AnnouncementDto> Items { get; set; } = Array.Empty<AnnouncementDto>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long TotalCount { get; set; }
}

public sealed class AnnouncementCategoryDto
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int AnnouncementCount { get; set; }
}

public sealed class AnnouncementStatsDto
{
    public int TotalAnnouncements { get; set; }
    public int TotalReads { get; set; }
    public int UnreadByMe { get; set; }
}

public sealed class AnnouncementUnreadCountDto
{
    public int UnreadCount { get; set; }
}

public sealed class AnnouncementMarkReadResult
{
    public long AnnouncementId { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

public sealed class AnnouncementPinResult
{
    public long AnnouncementId { get; set; }
    public bool IsPinned { get; set; }
}

public sealed class AnnouncementDeleteResult
{
    public long AnnouncementId { get; set; }
    public bool Deleted { get; set; }
}

public sealed class CreateAnnouncementRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? CategoryId { get; set; }
    public int? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public string? Location { get; set; }
    public bool IsImportant { get; set; }
    public int? StatusId { get; set; }
    public DateTimeOffset? ExpiryDate { get; set; }
    public IReadOnlyList<long>? RecipientUserIds { get; set; }
    public string? PostedByName { get; set; }
    public int? OrgId { get; set; }
    public int? AppId { get; set; }
    public int? FiscalYearId { get; set; }
}

public sealed class UpdateAnnouncementRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? CategoryId { get; set; }
    public int? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public string? Location { get; set; }
    public bool? IsImportant { get; set; }
    public int? StatusId { get; set; }
    public DateTimeOffset? ExpiryDate { get; set; }
    public IReadOnlyList<long>? RecipientUserIds { get; set; }
    public int? OrgId { get; set; }
    public int? AppId { get; set; }
    public int? FiscalYearId { get; set; }
}

public sealed class AnnouncementListQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Search { get; set; }
    public int? CategoryId { get; set; }
    public int? DepartmentId { get; set; }
    public string? Location { get; set; }
    public bool? Important { get; set; }
    public bool? Pinned { get; set; }
    public int? StatusId { get; set; }
    public string? Filter { get; set; }
    public string? SortBy { get; set; }
}
