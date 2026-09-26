using Chat.Application.Services.Interfaces.Messages;
using Chat.Domain.DTOs.Messages;
using Chat.Domain.Exceptions.Messages;
using Chat.Infrastructure.Repositories.Interfaces.Groups;
using Chat.Infrastructure.Repositories.Interfaces.Messages;

namespace Chat.Application.Services.Classes.Messages;

public sealed class ChatService : IChatService
{
    private readonly IChatRepository _chatRepository;
    private readonly IGroupRepository _groupRepository;

    public ChatService(IChatRepository chatRepository, IGroupRepository groupRepository)
    {
        _chatRepository = chatRepository;
        _groupRepository = groupRepository;
    }

    public async Task<PrivateChatDto> GetOrCreatePrivateChatAsync(
        long user1Id,
        long user2Id,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (user1Id <= 0 || user2Id <= 0)
            throw new ChatOperationException("user1Id and user2Id must be positive SoftOnCloud user ids.");

        if (user1Id == user2Id)
            throw new ChatOperationException("Cannot create a private chat with the same user.");

        if (orgId <= 0 || appId <= 0)
            throw new ChatOperationException("orgId and appId are required.");

        var chatId = await _chatRepository.GetOrCreatePrivateChatAsync(
            user1Id, user2Id, orgId, appId, fiscalYearId, cancellationToken);

        return new PrivateChatDto
        {
            ChatId = chatId,
            User1Id = user1Id,
            User2Id = user2Id
        };
    }

    public async Task<IReadOnlyList<ChatMessageDto>> GetChatMessagesAsync(
        int chatId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (chatId <= 0)
            throw new ChatOperationException("chatId is required.");

        if (authenticatedUserId <= 0)
            throw new ChatOperationException("Authenticated user id is required.", 401);

        if (orgId <= 0 || appId <= 0)
            throw new ChatOperationException("orgId and appId are required.");

        return await _chatRepository.GetChatMessagesAsync(
            chatId, authenticatedUserId, orgId, appId, fiscalYearId, cancellationToken);
    }

    public async Task<SentMessageDto> SendMessageAsync(
        int chatId,
        long authenticatedUserId,
        SendMessageRequest request,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (chatId <= 0)
            throw new ChatOperationException("chatId is required.");

        if (authenticatedUserId <= 0)
            throw new ChatOperationException("Authenticated user id is required.", 401);

        if (orgId <= 0 || appId <= 0)
            throw new ChatOperationException("orgId and appId are required.");

        if (request is null)
            throw new ChatOperationException("Request body is required.");

        var hasBody = !string.IsNullOrWhiteSpace(request.MessageBody);
        var hasAttachment =
            !string.IsNullOrWhiteSpace(request.AttachmentPath1)
            || !string.IsNullOrWhiteSpace(request.AttachmentPath2)
            || !string.IsNullOrWhiteSpace(request.AttachmentPath3)
            || !string.IsNullOrWhiteSpace(request.AttachmentPath4)
            || !string.IsNullOrWhiteSpace(request.AttachmentPath5);

        if (!hasBody && !hasAttachment)
            throw new ChatOperationException("messageBody or at least one attachment path is required.");

        if (request.MessageTypeId <= 0)
            throw new ChatOperationException("messageTypeId is required.");

        if (fiscalYearId is null or <= 0)
            throw new ChatOperationException("fiscalYearId is required.");

        var senderUserId = authenticatedUserId;
        if (request.SenderUserId > 0 && request.SenderUserId != authenticatedUserId)
            throw new ChatOperationException("senderUserId does not match the authenticated user.", 403);

        // ─── GROUP PATH (additive) ───────────────────────────────────────────
        long? groupId = request.GroupId is > 0 ? request.GroupId : null;
        if (groupId is null && request.ReceiverUserId <= 0)
        {
            groupId = await _groupRepository.GetGroupIdByChatIdAsync(
                chatId, orgId, appId, fiscalYearId.Value, cancellationToken);
        }

        if (groupId is > 0)
        {
            return await _chatRepository.SendMessageAsync(
                chatId,
                senderUserId,
                receiverUserId: null,
                hasBody ? request.MessageBody.Trim() : string.Empty,
                request.MessageTypeId,
                orgId,
                appId,
                fiscalYearId,
                NullIfWhiteSpace(request.AttachmentPath1),
                NullIfWhiteSpace(request.AttachmentPath2),
                NullIfWhiteSpace(request.AttachmentPath3),
                NullIfWhiteSpace(request.AttachmentPath4),
                NullIfWhiteSpace(request.AttachmentPath5),
                request.ParentMessageId is > 0 ? request.ParentMessageId : null,
                groupId,
                cancellationToken);
        }

        // ─── EXISTING ONE-TO-ONE PATH — DO NOT CHANGE ────────────────────────
        if (request.ReceiverUserId <= 0)
            throw new ChatOperationException("receiverUserId is required.");

        if (request.ReceiverUserId == senderUserId)
            throw new ChatOperationException("receiverUserId must be different from senderUserId.");

        return await _chatRepository.SendMessageAsync(
            chatId,
            senderUserId,
            request.ReceiverUserId,
            hasBody ? request.MessageBody.Trim() : string.Empty,
            request.MessageTypeId,
            orgId,
            appId,
            fiscalYearId,
            NullIfWhiteSpace(request.AttachmentPath1),
            NullIfWhiteSpace(request.AttachmentPath2),
            NullIfWhiteSpace(request.AttachmentPath3),
            NullIfWhiteSpace(request.AttachmentPath4),
            NullIfWhiteSpace(request.AttachmentPath5),
            request.ParentMessageId is > 0 ? request.ParentMessageId : null,
            groupId: null,
            cancellationToken);
    }

    public async Task<SentMessageDto> ForwardMessageAsync(
        long messageId,
        long authenticatedUserId,
        ForwardMessageRequest request,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (messageId <= 0)
            throw new ChatOperationException("messageId is required.");

        if (authenticatedUserId <= 0)
            throw new ChatOperationException("Authenticated user id is required.", 401);

        if (request is null)
            throw new ChatOperationException("Request body is required.");

        if (request.DestinationChatId <= 0)
            throw new ChatOperationException("destinationChatId is required.");

        // Private when receiver is set; otherwise treat as group destination.
        var isPrivateForward = request.ReceiverUserId is > 0;
        if (isPrivateForward && request.ReceiverUserId == authenticatedUserId)
            throw new ChatOperationException("receiverUserId must be different from the authenticated user.");

        if (orgId <= 0 || appId <= 0)
            throw new ChatOperationException("orgId and appId are required.");

        if (fiscalYearId is null or <= 0)
            throw new ChatOperationException("fiscalYearId is required.");

        return await _chatRepository.ForwardMessageAsync(
            messageId,
            request.DestinationChatId,
            authenticatedUserId,
            isPrivateForward ? request.ReceiverUserId!.Value : 0,
            orgId,
            appId,
            fiscalYearId,
            cancellationToken);
    }

    public async Task<DeleteMessageResult> DeleteMessageAsync(
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

        return await _chatRepository.DeleteMessageAsync(
            messageId, authenticatedUserId, orgId, appId, fiscalYearId, cancellationToken);
    }

    public async Task<ChatNotificationListResult> GetNotificationsAsync(
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (authenticatedUserId <= 0)
            throw new ChatOperationException("Authenticated user id is required.", 401);
        if (orgId <= 0 || appId <= 0)
            throw new ChatOperationException("orgId and appId are required.");
        if (fiscalYearId is null or <= 0)
            throw new ChatOperationException("fiscalYearId is required.");

        return await _chatRepository.GetNotificationsAsync(
            authenticatedUserId, orgId, appId, fiscalYearId, cancellationToken);
    }

    public async Task<ChatNotificationListResult> MarkNotificationReadAsync(
        long notificationId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (notificationId <= 0)
            throw new ChatOperationException("notificationId is required.");
        if (authenticatedUserId <= 0)
            throw new ChatOperationException("Authenticated user id is required.", 401);
        if (orgId <= 0 || appId <= 0)
            throw new ChatOperationException("orgId and appId are required.");
        if (fiscalYearId is null or <= 0)
            throw new ChatOperationException("fiscalYearId is required.");

        return await _chatRepository.MarkNotificationReadAsync(
            notificationId, authenticatedUserId, orgId, appId, fiscalYearId, cancellationToken);
    }

    public async Task<ChatNotificationDto> CreateMessageNotificationAsync(
        long receiverUserId,
        long senderUserId,
        string messageBody,
        long messageId,
        int orgId,
        int appId,
        int fiscalYearId,
        string? referenceType = null,
        CancellationToken cancellationToken = default,
        string? title = null)
    {
        if (receiverUserId <= 0 || senderUserId <= 0)
            throw new ChatOperationException("receiverUserId and senderUserId are required.");
        if (messageId <= 0)
            throw new ChatOperationException("messageId is required.");
        if (orgId <= 0 || appId <= 0 || fiscalYearId <= 0)
            throw new ChatOperationException("orgId, appId and fiscalYearId are required.");

        var preview = string.IsNullOrWhiteSpace(messageBody)
            ? "New message"
            : messageBody.Trim();
        if (preview.Length > 200)
            preview = preview[..200];

        var resolvedTitle = string.IsNullOrWhiteSpace(title) ? "New Message" : title.Trim();

        return await _chatRepository.CreateNotificationAsync(
            receiverUserId,
            senderUserId,
            resolvedTitle,
            preview,
            messageId,
            orgId,
            appId,
            fiscalYearId,
            cancellationToken,
            referenceType);
    }

    public async Task<PeerUnreadCountListResult> GetUnreadCountsByPeerAsync(
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (authenticatedUserId <= 0)
            throw new ChatOperationException("Authenticated user id is required.", 401);
        if (orgId <= 0 || appId <= 0)
            throw new ChatOperationException("orgId and appId are required.");

        return await _chatRepository.GetUnreadCountsByPeerAsync(
            authenticatedUserId, orgId, appId, fiscalYearId, cancellationToken);
    }

    public async Task<MarkMessagesReadResult> MarkMessagesReadAsync(
        int chatId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (chatId <= 0)
            throw new ChatOperationException("chatId is required.");
        if (authenticatedUserId <= 0)
            throw new ChatOperationException("Authenticated user id is required.", 401);
        if (orgId <= 0 || appId <= 0)
            throw new ChatOperationException("orgId and appId are required.");
        if (fiscalYearId is null or <= 0)
            throw new ChatOperationException("fiscalYearId is required.");

        return await _chatRepository.MarkMessagesReadAsync(
            chatId, authenticatedUserId, orgId, appId, fiscalYearId, cancellationToken);
    }

    public async Task<ToggleMessageStarResult> ToggleMessageStarAsync(
        long messageId,
        long authenticatedUserId,
        bool isStarred,
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

        return await _chatRepository.ToggleMessageStarAsync(
            messageId, authenticatedUserId, isStarred, orgId, appId, fiscalYearId, cancellationToken);
    }

    public async Task<ToggleMessageReactionResult> ToggleMessageReactionAsync(
        long messageId,
        long authenticatedUserId,
        string? reactionCode,
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

        if (!MessageReactionCodes.TryNormalize(reactionCode, out var code, out _))
            throw new ChatOperationException(
                $"Invalid reactionCode. Allowed: {MessageReactionCodes.AllowedList}.");

        return await _chatRepository.ToggleMessageReactionAsync(
            messageId, authenticatedUserId, code, orgId, appId, fiscalYearId, cancellationToken);
    }

    public async Task<MessageReactionListResult> GetMessageReactionsAsync(
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

        return await _chatRepository.GetMessageReactionsAsync(
            messageId, authenticatedUserId, orgId, appId, fiscalYearId, cancellationToken);
    }

    public async Task<ClearChatResult> ClearChatAsync(
        int chatId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (chatId <= 0)
            throw new ChatOperationException("chatId is required.");
        if (authenticatedUserId <= 0)
            throw new ChatOperationException("Authenticated user id is required.", 401);
        if (orgId <= 0 || appId <= 0)
            throw new ChatOperationException("orgId and appId are required.");
        if (fiscalYearId is null or <= 0)
            throw new ChatOperationException("fiscalYearId is required.");

        return await _chatRepository.ClearChatAsync(
            chatId, authenticatedUserId, orgId, appId, fiscalYearId, cancellationToken);
    }

    public async Task<DeleteChatResult> DeleteChatAsync(
        int chatId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (chatId <= 0)
            throw new ChatOperationException("chatId is required.");
        if (authenticatedUserId <= 0)
            throw new ChatOperationException("Authenticated user id is required.", 401);
        if (orgId <= 0 || appId <= 0)
            throw new ChatOperationException("orgId and appId are required.");
        if (fiscalYearId is null or <= 0)
            throw new ChatOperationException("fiscalYearId is required.");

        return await _chatRepository.DeleteChatAsync(
            chatId, authenticatedUserId, orgId, appId, fiscalYearId, cancellationToken);
    }

    public async Task<IReadOnlyList<ChatMessageDto>> GetStarredMessagesAsync(
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default)
    {
        if (authenticatedUserId <= 0)
            throw new ChatOperationException("Authenticated user id is required.", 401);
        if (orgId <= 0 || appId <= 0)
            throw new ChatOperationException("orgId and appId are required.");
        if (fiscalYearId is null or <= 0)
            throw new ChatOperationException("fiscalYearId is required.");

        return await _chatRepository.GetStarredMessagesAsync(
            authenticatedUserId, orgId, appId, fiscalYearId, cancellationToken);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
