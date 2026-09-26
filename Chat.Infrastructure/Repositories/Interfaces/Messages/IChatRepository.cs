using Chat.Domain.DTOs.Messages;

namespace Chat.Infrastructure.Repositories.Interfaces.Messages;

public interface IChatRepository
{
    Task<int> GetOrCreatePrivateChatAsync(
        long user1Id,
        long user2Id,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatMessageDto>> GetChatMessagesAsync(
        int chatId,
        long userId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<SentMessageDto> SendMessageAsync(
        int chatId,
        long senderUserId,
        long? receiverUserId,
        string messageBody,
        short messageTypeId,
        int orgId,
        int appId,
        int? fiscalYearId,
        string? attachmentPath1 = null,
        string? attachmentPath2 = null,
        string? attachmentPath3 = null,
        string? attachmentPath4 = null,
        string? attachmentPath5 = null,
        long? parentMessageId = null,
        long? groupId = null,
        CancellationToken cancellationToken = default);

    Task<SentMessageDto> ForwardMessageAsync(
        long originalMessageId,
        int destinationChatId,
        long forwardedByUserId,
        long receiverUserId,
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

    Task<ChatNotificationDto> CreateNotificationAsync(
        long receiverUserId,
        long senderUserId,
        string title,
        string message,
        long referenceId,
        int orgId,
        int appId,
        int fiscalYearId,
        CancellationToken cancellationToken = default,
        string? referenceType = null,
        string? notificationType = null,
        string? referenceEntity = null);

    Task<ChatNotificationListResult> GetNotificationsAsync(
        long userId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<ChatNotificationListResult> MarkNotificationReadAsync(
        long notificationId,
        long userId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<MarkMessagesReadResult> MarkMessagesReadAsync(
        int chatId,
        long readerUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken = default);

    Task<PeerUnreadCountListResult> GetUnreadCountsByPeerAsync(
        long userId,
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

    /// <summary>WhatsApp-style: all users who reacted to a message.</summary>
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
