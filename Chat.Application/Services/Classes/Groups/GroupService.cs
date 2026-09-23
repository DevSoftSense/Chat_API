using Chat.Application.Services.Interfaces.Groups;
using Chat.Domain.DTOs.Groups;
using Chat.Domain.Exceptions.Messages;
using Chat.Infrastructure.Repositories.Interfaces.Groups;

namespace Chat.Application.Services.Classes.Groups;

public sealed class GroupService : IGroupService
{
    private readonly IGroupRepository _groupRepository;

    public GroupService(IGroupRepository groupRepository)
    {
        _groupRepository = groupRepository;
    }

    public async Task<ChatGroupDto> CreateGroupAsync(
        long authenticatedUserId,
        CreateGroupRequest request,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (authenticatedUserId <= 0)
            throw new ChatOperationException("Authenticated user id is required.", 401);
        if (request is null)
            throw new ChatOperationException("Request body is required.");
        if (string.IsNullOrWhiteSpace(request.GroupName))
            throw new ChatOperationException("groupName is required.");
        if (string.IsNullOrWhiteSpace(request.GroupCode))
            throw new ChatOperationException("groupCode is required.");
        if (orgId <= 0 || appId <= 0 || fiscalYearId <= 0)
            throw new ChatOperationException("orgId, appId and fiscalYearId are required.");

        var members = (request.MemberUserIds ?? Array.Empty<long>())
            .Where(id => id > 0 && id != authenticatedUserId)
            .Distinct()
            .ToList();

        return await _groupRepository.CreateGroupAsync(
            request.GroupName.Trim(),
            request.GroupCode.Trim(),
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            authenticatedUserId,
            members,
            orgId,
            appId,
            fiscalYearId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ChatGroupDto>> GetUserGroupsAsync(
        long authenticatedUserId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (authenticatedUserId <= 0)
            throw new ChatOperationException("Authenticated user id is required.", 401);
        if (orgId <= 0 || appId <= 0 || fiscalYearId <= 0)
            throw new ChatOperationException("orgId, appId and fiscalYearId are required.");

        return await _groupRepository.GetUserGroupsAsync(
            authenticatedUserId, orgId, appId, fiscalYearId, cancellationToken);
    }

    public async Task<ChatGroupDetailsDto> GetGroupDetailsAsync(
        long groupId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (groupId <= 0)
            throw new ChatOperationException("groupId is required.");
        if (authenticatedUserId <= 0)
            throw new ChatOperationException("Authenticated user id is required.", 401);
        if (orgId <= 0 || appId <= 0 || fiscalYearId <= 0)
            throw new ChatOperationException("orgId, appId and fiscalYearId are required.");

        return await _groupRepository.GetGroupDetailsAsync(
            groupId, authenticatedUserId, orgId, appId, fiscalYearId, cancellationToken);
    }

    public async Task<IReadOnlyList<GroupMemberChangeResult>> AddGroupMembersAsync(
        long groupId,
        long authenticatedUserId,
        AddGroupMembersRequest request,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (groupId <= 0)
            throw new ChatOperationException("groupId is required.");
        if (authenticatedUserId <= 0)
            throw new ChatOperationException("Authenticated user id is required.", 401);
        if (request is null)
            throw new ChatOperationException("Request body is required.");
        if (orgId <= 0 || appId <= 0 || fiscalYearId <= 0)
            throw new ChatOperationException("orgId, appId and fiscalYearId are required.");

        var members = (request.MemberUserIds ?? Array.Empty<long>())
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (members.Count == 0)
            throw new ChatOperationException("memberUserIds is required.");

        return await _groupRepository.AddGroupMembersAsync(
            groupId, members, authenticatedUserId, orgId, appId, fiscalYearId, cancellationToken);
    }

    public async Task<GroupMemberChangeResult> RemoveGroupMemberAsync(
        long groupId,
        long targetUserId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (groupId <= 0)
            throw new ChatOperationException("groupId is required.");
        if (targetUserId <= 0)
            throw new ChatOperationException("userId is required.");
        if (authenticatedUserId <= 0)
            throw new ChatOperationException("Authenticated user id is required.", 401);
        if (orgId <= 0 || appId <= 0 || fiscalYearId <= 0)
            throw new ChatOperationException("orgId, appId and fiscalYearId are required.");

        return await _groupRepository.RemoveGroupMemberAsync(
            groupId, targetUserId, authenticatedUserId, orgId, appId, fiscalYearId, cancellationToken);
    }

    public async Task<ChatGroupDto> UpdateGroupAsync(
        long groupId,
        long authenticatedUserId,
        UpdateGroupRequest request,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (groupId <= 0)
            throw new ChatOperationException("groupId is required.");
        if (authenticatedUserId <= 0)
            throw new ChatOperationException("Authenticated user id is required.", 401);
        if (request is null)
            throw new ChatOperationException("Request body is required.");
        if (string.IsNullOrWhiteSpace(request.GroupName))
            throw new ChatOperationException("groupName is required.");
        if (orgId <= 0 || appId <= 0 || fiscalYearId <= 0)
            throw new ChatOperationException("orgId, appId and fiscalYearId are required.");

        return await _groupRepository.UpdateGroupAsync(
            groupId,
            request.GroupName.Trim(),
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            request.ProfilePic,
            authenticatedUserId,
            orgId,
            appId,
            fiscalYearId,
            cancellationToken);
    }

    public async Task<GroupMemberChangeResult> LeaveGroupAsync(
        long groupId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (groupId <= 0)
            throw new ChatOperationException("groupId is required.");
        if (authenticatedUserId <= 0)
            throw new ChatOperationException("Authenticated user id is required.", 401);
        if (orgId <= 0 || appId <= 0 || fiscalYearId <= 0)
            throw new ChatOperationException("orgId, appId and fiscalYearId are required.");

        return await _groupRepository.LeaveGroupAsync(
            groupId, authenticatedUserId, orgId, appId, fiscalYearId, cancellationToken);
    }

    public async Task AssertGroupMemberAsync(
        long groupId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (groupId <= 0)
            throw new ChatOperationException("groupId is required.");
        if (authenticatedUserId <= 0)
            throw new ChatOperationException("Authenticated user id is required.", 401);
        if (orgId <= 0 || appId <= 0 || fiscalYearId <= 0)
            throw new ChatOperationException("orgId, appId and fiscalYearId are required.");

        await _groupRepository.AssertGroupMemberAsync(
            groupId, authenticatedUserId, orgId, appId, fiscalYearId, cancellationToken);
    }

    public async Task<MessageInfoResult> GetMessageInfoAsync(
        long messageId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (messageId <= 0)
            throw new ChatOperationException("messageId is required.");
        if (authenticatedUserId <= 0)
            throw new ChatOperationException("Authenticated user id is required.", 401);
        if (orgId <= 0 || appId <= 0)
            throw new ChatOperationException("orgId and appId are required.");
        if (fiscalYearId is null or <= 0)
            throw new ChatOperationException("fiscalYearId is required.");

        return await _groupRepository.GetMessageInfoAsync(
            messageId, authenticatedUserId, orgId, appId, fiscalYearId, cancellationToken);
    }

    public async Task MarkMessageDeliveredAsync(
        long messageId,
        long receiverUserId,
        int chatId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (messageId <= 0 || receiverUserId <= 0 || chatId <= 0)
            return;

        await _groupRepository.MarkMessageDeliveredAsync(
            messageId, receiverUserId, chatId, orgId, appId, fiscalYearId, cancellationToken);
    }
}
