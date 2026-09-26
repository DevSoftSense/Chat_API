using Chat.Domain.DTOs.Groups;

namespace Chat.Infrastructure.Repositories.Interfaces.Groups;

public interface IGroupRepository
{
    Task<ChatGroupDto> CreateGroupAsync(
        string groupName,
        string groupCode,
        string? description,
        long createdByUserId,
        IReadOnlyList<long> memberUserIds,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatGroupDto>> GetUserGroupsAsync(
        long userId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<ChatGroupDetailsDto> GetGroupDetailsAsync(
        long groupId,
        long userId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GroupMemberChangeResult>> AddGroupMembersAsync(
        long groupId,
        IReadOnlyList<long> memberUserIds,
        long requestingUserId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<GroupMemberChangeResult> RemoveGroupMemberAsync(
        long groupId,
        long targetUserId,
        long requestingUserId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default);

    /// <summary>Admin-only: promote or demote member as group admin (multi-admin).</summary>
    Task<GroupMemberChangeResult> SetGroupMemberAdminAsync(
        long groupId,
        long targetUserId,
        long requestingUserId,
        bool isAdmin,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<ChatGroupDto> UpdateGroupAsync(
        long groupId,
        string groupName,
        string? description,
        string? profilePic,
        long requestingUserId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<GroupMemberChangeResult> LeaveGroupAsync(
        long groupId,
        long userId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<long?> GetGroupIdByChatIdAsync(
        int chatId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default);

    Task AssertGroupMemberAsync(
        long groupId,
        long userId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default);

    /// <summary>WhatsApp-style Message Info (Read by / Delivered to).</summary>
    Task<MessageInfoResult> GetMessageInfoAsync(
        long messageId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);

    /// <summary>Persist group delivered_at only — does not mark read.</summary>
    Task MarkMessageDeliveredAsync(
        long messageId,
        long receiverUserId,
        int chatId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);
}
