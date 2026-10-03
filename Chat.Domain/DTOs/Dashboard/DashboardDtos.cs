namespace Chat.Domain.DTOs.Dashboard;

public sealed class DashboardResponseDto
{
    public DashboardSummaryDto Summary { get; set; } = new();
    public IReadOnlyList<DashboardRecentMessageDto> RecentMessages { get; set; } = Array.Empty<DashboardRecentMessageDto>();
    /// <summary>Unread inbox items (Messages + Groups) for dashboard popup.</summary>
    public IReadOnlyList<DashboardRecentMessageDto> UnreadMessageList { get; set; } = Array.Empty<DashboardRecentMessageDto>();
    public IReadOnlyList<DashboardRecentSentMessageDto> RecentSentMessages { get; set; } = Array.Empty<DashboardRecentSentMessageDto>();
    /// <summary>Messages I sent today — for Sent Today popup.</summary>
    public IReadOnlyList<DashboardRecentSentMessageDto> SentTodayList { get; set; } = Array.Empty<DashboardRecentSentMessageDto>();
    public IReadOnlyList<DashboardDepartmentSummaryDto> DepartmentMessageSummary { get; set; } = Array.Empty<DashboardDepartmentSummaryDto>();
    public IReadOnlyList<DashboardAnnouncementDto> RecentAnnouncements { get; set; } = Array.Empty<DashboardAnnouncementDto>();
    /// <summary>Unread announcements for current user — dashboard Announcements popup.</summary>
    public IReadOnlyList<DashboardAnnouncementDto> UnreadAnnouncementList { get; set; } = Array.Empty<DashboardAnnouncementDto>();
    public DashboardReadUnreadDto ReadUnreadSummary { get; set; } = new();
    public IReadOnlyList<DashboardWeeklyPointDto> WeeklyAnalytics { get; set; } = Array.Empty<DashboardWeeklyPointDto>();
    public DashboardWeeklyTotalsDto WeeklyTotals { get; set; } = new();
    /// <summary>Current calendar month — one point per day.</summary>
    public IReadOnlyList<DashboardWeeklyPointDto> MonthlyAnalytics { get; set; } = Array.Empty<DashboardWeeklyPointDto>();
    public DashboardWeeklyTotalsDto MonthlyTotals { get; set; } = new();
    /// <summary>Current calendar year — one point per month (Jan–Dec).</summary>
    public IReadOnlyList<DashboardWeeklyPointDto> YearlyAnalytics { get; set; } = Array.Empty<DashboardWeeklyPointDto>();
    public DashboardWeeklyTotalsDto YearlyTotals { get; set; } = new();
    public DashboardProfileDto Profile { get; set; } = new();
    public IReadOnlyList<DashboardFavoriteContactDto> FavoriteContacts { get; set; } = Array.Empty<DashboardFavoriteContactDto>();
    /// <summary>Org employee user ids — used by UI to intersect SignalR OnlineUsers.</summary>
    public IReadOnlyList<long> EmployeeUserIds { get; set; } = Array.Empty<long>();
}

public sealed class DashboardSummaryDto
{
    public int UnreadMessages { get; set; }
    public int SentToday { get; set; }
    public int OnlineEmployees { get; set; }
    public int TotalEmployees { get; set; }
    public int PendingReads { get; set; }
    public int Announcements { get; set; }
    public int Drafts { get; set; }
}

public sealed class DashboardRecentMessageDto
{
    public long MessageId { get; set; }
    public long? ChatId { get; set; }
    public long SenderUserId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string? SenderPhotoUrl { get; set; }
    public string Preview { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; }
    public bool IsUnread { get; set; }
    public bool IsGroup { get; set; }
    public string? GroupName { get; set; }
    public long? GroupId { get; set; }
    /// <summary>True when this row is about a message I sent that others have not read.</summary>
    public bool IsOutgoing { get; set; }
}

public sealed class DashboardRecentSentMessageDto
{
    public long MessageId { get; set; }
    public long? ChatId { get; set; }
    public long? RecipientUserId { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string? RecipientPhotoUrl { get; set; }
    public string Preview { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; }
    public bool IsGroup { get; set; }
    public long? GroupId { get; set; }
    public bool IsRead { get; set; }
    public string? ReadStatus { get; set; }
}

public sealed class DashboardDepartmentSummaryDto
{
    public string Department { get; set; } = string.Empty;
    public int MessageCount { get; set; }
    public decimal Percentage { get; set; }
}

public sealed class DashboardAnnouncementDto
{
    public long AnnouncementId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CategoryName { get; set; }
    public string? DepartmentName { get; set; }
    public string? StatusName { get; set; }
    public int StatusId { get; set; }
    public DateTimeOffset PostedDate { get; set; }
    public DateTimeOffset? ExpiryDate { get; set; }
    public bool IsImportant { get; set; }
    public bool IsPinned { get; set; }
    public long PostedBy { get; set; }
    public string? PostedByName { get; set; }
}

public sealed class DashboardReadUnreadDto
{
    public int Read { get; set; }
    public int Unread { get; set; }
}

public sealed class DashboardWeeklyPointDto
{
    public string Day { get; set; } = string.Empty;
    public int DayOfWeek { get; set; }
    public int Count { get; set; }
    public DateOnly Date { get; set; }
}

public sealed class DashboardWeeklyTotalsDto
{
    public int Sent { get; set; }
    public int Received { get; set; }
    public int Replies { get; set; }
}

public sealed class DashboardProfileDto
{
    public long UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;
    public string Status { get; set; } = "Online";
}

/// <summary>
/// Reserved for future favorite-contact support. Currently always empty —
/// no favorite-contacts table/API exists in this product.
/// </summary>
public sealed class DashboardFavoriteContactDto
{
    public long UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string Status { get; set; } = "offline";
}
