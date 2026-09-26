using Chat.Domain.DTOs.Messages;

namespace Chat.Application.Services.Interfaces.Messages;

public interface IChatService
{
    Task<PrivateChatDto> GetOrCreatePrivateChatAsync(
        long user1Id,
        long user2Id,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatMessageDto>> GetChatMessagesAsync(
        int chatId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<SentMessageDto> SendMessageAsync(
        int chatId,
        long authenticatedUserId,
        SendMessageRequest request,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<SentMessageDto> ForwardMessageAsync(
        long messageId,
        long authenticatedUserId,
        ForwardMessageRequest request,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<DeleteMessageResult> DeleteMessageAsync(
        long messageId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<ChatNotificationListResult> GetNotificationsAsync(
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<ChatNotificationListResult> MarkNotificationReadAsync(
        long notificationId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<ChatNotificationDto> CreateMessageNotificationAsync(
        long receiverUserId,
        long senderUserId,
        string messageBody,
        long messageId,
        int orgId,
        int appId,
        int fiscalYearId,
        string? referenceType = null,
        CancellationToken cancellationToken = default,
        string? title = null);

    Task<MarkMessagesReadResult> MarkMessagesReadAsync(
        int chatId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<PeerUnreadCountListResult> GetUnreadCountsByPeerAsync(
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<ToggleMessageStarResult> ToggleMessageStarAsync(
        long messageId,
        long authenticatedUserId,
        bool isStarred,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<ToggleMessageReactionResult> ToggleMessageReactionAsync(
        long messageId,
        long authenticatedUserId,
        string? reactionCode,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);

    /// <summary>WhatsApp-style: who reacted to this message.</summary>
    Task<MessageReactionListResult> GetMessageReactionsAsync(
        long messageId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<ClearChatResult> ClearChatAsync(
        int chatId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<DeleteChatResult> DeleteChatAsync(
        int chatId,
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatMessageDto>> GetStarredMessagesAsync(
        long authenticatedUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);
}
