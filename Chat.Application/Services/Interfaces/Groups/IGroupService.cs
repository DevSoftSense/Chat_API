using Chat.Domain.DTOs.Groups;

namespace Chat.Application.Services.Interfaces.Groups;

public interface IGroupService
{
    Task<ChatGroupDto> CreateGroupAsync(
        long authenticatedUserId,
        CreateGroupRequest request,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatGroupDto>> GetUserGroupsAsync(
        long authenticatedUserId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<ChatGroupDetailsDto> GetGroupDetailsAsync(
        long groupId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GroupMemberChangeResult>> AddGroupMembersAsync(
        long groupId,
        long authenticatedUserId,
        AddGroupMembersRequest request,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<GroupMemberChangeResult> RemoveGroupMemberAsync(
        long groupId,
        long targetUserId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<ChatGroupDto> UpdateGroupAsync(
        long groupId,
        long authenticatedUserId,
        UpdateGroupRequest request,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<GroupMemberChangeResult> LeaveGroupAsync(
        long groupId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default);

    Task AssertGroupMemberAsync(
        long groupId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default);

    /// <summary>WhatsApp-style Message Info for a group message.</summary>
    Task<MessageInfoResult> GetMessageInfoAsync(
        long messageId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);

    /// <summary>Group Message Info: delivered without read.</summary>
    Task MarkMessageDeliveredAsync(
        long messageId,
        long receiverUserId,
        int chatId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);
}
