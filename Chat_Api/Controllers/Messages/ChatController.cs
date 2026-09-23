using Chat.Application.Services.Interfaces.Groups;
using Chat.Application.Services.Interfaces.Messages;
using Chat.Application.Services.Interfaces.Organisation;
using Chat.Domain.DTOs.Messages;
using Chat.Domain.Exceptions.Messages;
using Chat.Infrastructure.Repositories.Interfaces.Groups;
using Chat_Api.Helpers;
using Chat_Api.Hubs.Messages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;

namespace Chat_Api.Controllers.Messages;

[Authorize]
[ApiController]
[Route("api/chat")]
public sealed class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly IGroupService _groupService;
    private readonly IOrganisationSettingService _organisationSettingService;
    private readonly IGroupRepository _groupRepository;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly IConfiguration _configuration;
    private readonly ChatAttachmentStorage _attachmentStorage;

    public ChatController(
        IChatService chatService,
        IGroupService groupService,
        IOrganisationSettingService organisationSettingService,
        IGroupRepository groupRepository,
        IHubContext<ChatHub> hubContext,
        IConfiguration configuration,
        ChatAttachmentStorage attachmentStorage)
    {
        _chatService = chatService;
        _groupService = groupService;
        _organisationSettingService = organisationSettingService;
        _groupRepository = groupRepository;
        _hubContext = hubContext;
        _configuration = configuration;
        _attachmentStorage = attachmentStorage;
    }

    /// <summary>
    /// Get or create the private 1-to-1 conversation between two SoftOnCloud user ids.
    /// </summary>
    [HttpGet("private/{user1Id:long}/{user2Id:long}")]
    public async Task<IActionResult> GetOrCreatePrivateChat(
        long user1Id,
        long user2Id,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            var result = await _chatService.GetOrCreatePrivateChatAsync(
                user1Id, user2Id, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, cancellationToken);

            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Return non-draft messages for a private chat (participant only).
    /// </summary>
    [HttpGet("{chatId:int}/messages")]
    public async Task<IActionResult> GetMessages(
        int chatId,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            var messages = await _chatService.GetChatMessagesAsync(
                chatId, userId.Value, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, cancellationToken);

            // WhatsApp-style: opening a group chat marks inbound messages read (receipts in DB).
            try
            {
                var readResult = await _chatService.MarkMessagesReadAsync(
                    chatId,
                    userId.Value,
                    tenant.OrgId,
                    tenant.AppId,
                    tenant.FiscalYearId,
                    cancellationToken);

                if (readResult.MessageIds.Count > 0)
                {
                    var payload = new
                    {
                        chatId = readResult.ChatId,
                        readerUserId = readResult.ReaderUserId,
                        readAt = readResult.ReadAt,
                        messageIds = readResult.MessageIds
                    };

                    foreach (var senderId in readResult.SenderUserIds.Distinct())
                    {
                        if (senderId <= 0 || senderId == readResult.ReaderUserId) continue;
                        await _hubContext.Clients
                            .Group(ChatHub.UserGroup(senderId))
                            .SendAsync("MessagesRead", payload, cancellationToken);
                    }

                    await _hubContext.Clients
                        .Group(ChatHub.UserGroup(readResult.ReaderUserId))
                        .SendAsync("MessagesRead", payload, cancellationToken);
                }
            }
            catch (Exception)
            {
                // Messages already returned — receipt errors are logged in repository.
            }

            return Ok(messages);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Save a message and push it to the receiver over SignalR.
    /// Tenant scope (orgId, appId, fiscalYearId): prefer [FromQuery], else body, else JWT claims.
    /// </summary>
    [HttpPost("{chatId:int}/messages")]
    public async Task<IActionResult> SendMessage(
        int chatId,
        [FromBody] SendMessageRequest request,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
        {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            // Query wins; body is accepted for Postman-style JSON payloads.
            var tenant = ResolveTenant(
                orgId ?? request?.OrgId,
                appId ?? request?.AppId,
                fiscalYearId ?? request?.FiscalYearId);

            var saved = await _chatService.SendMessageAsync(
                chatId, userId.Value, request!, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, cancellationToken);

            var payload = BuildReceiveMessagePayload(saved);

            // ─── GROUP realtime + notifications (same as 1:1 for other members) ─
            if (saved.GroupId is > 0)
            {
                await NotifyGroupMessageAsync(
                    saved, payload, userId.Value, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, cancellationToken);
                return Ok(saved);
            }

            // ─── EXISTING ONE-TO-ONE SignalR — unchanged ──────────────────────
            if (saved.ReceiverUserId is not > 0)
                return Ok(saved);

            await _hubContext.Clients
                .Group(ChatHub.UserGroup(saved.ReceiverUserId.Value))
                .SendAsync("ReceiveMessage", payload, cancellationToken);

            await _hubContext.Clients
                .Group(ChatHub.UserGroup(saved.SenderUserId))
                .SendAsync("ReceiveMessage", payload, cancellationToken);

            // Online receiver ⇒ delivered (double grey), not read.
            if (ChatHub.IsUserOnline(saved.ReceiverUserId.Value))
            {
                await _hubContext.Clients
                    .Group(ChatHub.UserGroup(saved.SenderUserId))
                    .SendAsync(
                        "MessageDelivered",
                        new
                        {
                            messageId = saved.MessageId,
                            chatId = saved.ChatId,
                            receiverUserId = saved.ReceiverUserId,
                            senderUserId = saved.SenderUserId
                        },
                        cancellationToken);
            }

            // Notification only after successful message insert — receiver only
            try
            {
                if (tenant.FiscalYearId is > 0)
                {
                    var notification = await _chatService.CreateMessageNotificationAsync(
                        saved.ReceiverUserId.Value,
                        saved.SenderUserId,
                        saved.MessageBody,
                        saved.MessageId,
                        tenant.OrgId,
                        tenant.AppId,
                        tenant.FiscalYearId.Value,
                        cancellationToken: cancellationToken);

                    var notificationPayload = new
                    {
                        notificationId = notification.NotificationId,
                        notificationType = notification.NotificationType,
                        title = notification.Title,
                        message = notification.Message,
                        referenceId = notification.ReferenceId,
                        referenceType = notification.ReferenceType,
                        senderUserId = notification.SenderUserId,
                        createdDate = notification.CreatedDate,
                        isRead = notification.IsRead
                    };

                    await _hubContext.Clients
                        .Group(ChatHub.UserGroup(saved.ReceiverUserId.Value))
                        .SendAsync("ReceiveNotification", notificationPayload, cancellationToken);
                }
            }
            catch (Exception)
            {
                // Message already saved — do not fail the send API if notification fails.
            }

            return Ok(saved);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Unread inbound message counts grouped by peer (for chat-list green badges).
    /// </summary>
    [HttpGet("unread-counts")]
    public async Task<IActionResult> GetUnreadCountsByPeer(
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            var result = await _chatService.GetUnreadCountsByPeerAsync(
                userId.Value, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, cancellationToken);

            return Ok(new
            {
                items = result.Items.Select(i => new
                {
                    peerUserId = i.PeerUserId,
                    unreadCount = i.UnreadCount
                }),
                totalUnread = result.TotalUnread
            });
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Mark unread messages in a chat as read for the authenticated receiver.
    /// Broadcasts MessagesRead to senders on the existing ChatHub.
    /// </summary>
    [HttpPost("{chatId:int}/messages/read")]
    public async Task<IActionResult> MarkMessagesRead(
        int chatId,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            var result = await _chatService.MarkMessagesReadAsync(
                chatId, userId.Value, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, cancellationToken);

            if (result.MessageIds.Count > 0)
            {
                var payload = new
                {
                    chatId = result.ChatId,
                    readerUserId = result.ReaderUserId,
                    readAt = result.ReadAt,
                    messageIds = result.MessageIds
                };

                foreach (var senderId in result.SenderUserIds.Distinct())
                {
                    if (senderId <= 0 || senderId == result.ReaderUserId) continue;
                    await _hubContext.Clients
                        .Group(ChatHub.UserGroup(senderId))
                        .SendAsync("MessagesRead", payload, cancellationToken);
                }

                // Also notify the reader's other tabs
                await _hubContext.Clients
                    .Group(ChatHub.UserGroup(result.ReaderUserId))
                    .SendAsync("MessagesRead", payload, cancellationToken);
            }

            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Notifications for the authenticated user + dynamic unread count.
    /// </summary>
    [HttpGet("notifications")]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            var result = await _chatService.GetNotificationsAsync(
                userId.Value, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, cancellationToken);

            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Mark a notification as read (owner only) and return updated unread count.
    /// </summary>
    [HttpPost("notifications/{notificationId:long}/read")]
    public async Task<IActionResult> MarkNotificationRead(
        long notificationId,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            var result = await _chatService.MarkNotificationReadAsync(
                notificationId, userId.Value, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, cancellationToken);

            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Returns the configured chat attachment size limit (ChatMaxFileSizeMB from tab_organisation_setting).
    /// </summary>
    [HttpGet("attachments/limits")]
    public async Task<IActionResult> GetAttachmentLimits(
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            var maxFileSizeMB = await _organisationSettingService.GetChatMaxFileSizeMBAsync(
                tenant.OrgId, tenant.AppId, cancellationToken);

            return Ok(new
            {
                maxFileSizeMB,
                maxBytes = (long)maxFileSizeMB * 1024L * 1024L
            });
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Upload chat attachment files to wwwroot storage. Returns web-relative paths for attachment_path_1..5.
    /// Does not write to PostgreSQL — paths are saved later via send/forward message APIs.
    /// Size limit is read from organisation setting ChatMaxFileSizeMB (fallback 60 MB).
    /// </summary>
    [HttpPost("attachments/upload")]
    [RequestSizeLimit(104857600)]
    [RequestFormLimits(MultipartBodyLengthLimit = 104857600)]
    public async Task<IActionResult> UploadAttachments(
        [FromForm] List<IFormFile>? files,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            var maxFileSizeMB = await _organisationSettingService.GetChatMaxFileSizeMBAsync(
                tenant.OrgId, tenant.AppId, cancellationToken);

            var uploaded = files is { Count: > 0 }
                ? files
                : Request.Form.Files.ToList();

            var results = await _attachmentStorage.SaveAsync(
                uploaded, Request, maxFileSizeMB, cancellationToken);
            return Ok(new
            {
                files = results,
                maxFileSizeMB,
                // Convenience: first five paths mapped to attachment slots.
                attachmentPath1 = results.ElementAtOrDefault(0)?.FilePath,
                attachmentPath2 = results.ElementAtOrDefault(1)?.FilePath,
                attachmentPath3 = results.ElementAtOrDefault(2)?.FilePath,
                attachmentPath4 = results.ElementAtOrDefault(3)?.FilePath,
                attachmentPath5 = results.ElementAtOrDefault(4)?.FilePath
            });
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Download a chat attachment as a browser file download (Content-Disposition: attachment).
    /// Uses authenticated API so the UI does not depend on static-file CORS for fetch().
    /// </summary>
    [HttpGet("attachments/download")]
    public IActionResult DownloadAttachment([FromQuery] string path)
    {
        var userId = CurrentUserHelper.GetUserId(User);
        if (userId is null)
            return Unauthorized(new { message = "Authenticated user id claim is missing." });

        if (!_attachmentStorage.TryResolvePhysicalPath(path, out var physicalPath, out var downloadFileName))
            return NotFound(new { message = "Attachment file was not found." });

        var contentType = ResolveContentType(downloadFileName);
        return PhysicalFile(physicalPath, contentType, fileDownloadName: downloadFileName);
    }

    private static string ResolveContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Forward an existing message into a destination private chat (inserts a NEW tab_messages row).
    /// </summary>
    [HttpPost("messages/{messageId:long}/forward")]
    public async Task<IActionResult> ForwardMessage(
        long messageId,
        [FromBody] ForwardMessageRequest request,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(
                orgId ?? request?.OrgId,
                appId ?? request?.AppId,
                fiscalYearId ?? request?.FiscalYearId);

            var saved = await _chatService.ForwardMessageAsync(
                messageId, userId.Value, request!, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, cancellationToken);

            var payload = BuildReceiveMessagePayload(saved);

            // Group forward — same realtime + notifications as SendMessage group path.
            if (saved.GroupId is > 0)
            {
                await NotifyGroupMessageAsync(
                    saved, payload, userId.Value, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, cancellationToken);
                return Ok(saved);
            }

            if (saved.ReceiverUserId is not > 0)
                return Ok(saved);

            await _hubContext.Clients
                .Group(ChatHub.UserGroup(saved.ReceiverUserId.Value))
                .SendAsync("ReceiveMessage", payload, cancellationToken);

            await _hubContext.Clients
                .Group(ChatHub.UserGroup(saved.SenderUserId))
                .SendAsync("ReceiveMessage", payload, cancellationToken);

            if (ChatHub.IsUserOnline(saved.ReceiverUserId.Value))
            {
                await _hubContext.Clients
                    .Group(ChatHub.UserGroup(saved.SenderUserId))
                    .SendAsync(
                        "MessageDelivered",
                        new
                        {
                            messageId = saved.MessageId,
                            chatId = saved.ChatId,
                            receiverUserId = saved.ReceiverUserId,
                            senderUserId = saved.SenderUserId
                        },
                        cancellationToken);
            }

            try
            {
                if (tenant.FiscalYearId is > 0)
                {
                    var preview = string.IsNullOrWhiteSpace(saved.MessageBody)
                        ? "Forwarded message"
                        : saved.MessageBody;

                    var notification = await _chatService.CreateMessageNotificationAsync(
                        saved.ReceiverUserId.Value,
                        saved.SenderUserId,
                        preview,
                        saved.MessageId,
                        tenant.OrgId,
                        tenant.AppId,
                        tenant.FiscalYearId.Value,
                        cancellationToken: cancellationToken);

                    await _hubContext.Clients
                        .Group(ChatHub.UserGroup(saved.ReceiverUserId.Value))
                        .SendAsync(
                            "ReceiveNotification",
                            new
                            {
                                notificationId = notification.NotificationId,
                                notificationType = notification.NotificationType,
                                title = notification.Title,
                                message = notification.Message,
                                referenceId = notification.ReferenceId,
                                referenceType = notification.ReferenceType,
                                senderUserId = notification.SenderUserId,
                                createdDate = notification.CreatedDate,
                                isRead = notification.IsRead
                            },
                            cancellationToken);
                }
            }
            catch (Exception)
            {
                // Forward already saved — do not fail the API if notification fails.
            }

            return Ok(saved);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Soft-delete a message for everyone (delete_flag = 1). Never physically deletes the row.
    /// </summary>
    [HttpDelete("messages/{messageId:long}")]
    public async Task<IActionResult> DeleteMessage(
        long messageId,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            var result = await _chatService.DeleteMessageAsync(
                messageId, userId.Value, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, cancellationToken);

            var payload = new
            {
                messageId = result.MessageId,
                chatId = result.ChatId,
                senderUserId = result.SenderUserId,
                receiverUserId = result.ReceiverUserId,
                deletedBy = result.DeletedBy,
                deletedAt = result.DeletedAt,
                deleteFlag = result.DeleteFlag
            };

            // Group chat: notify everyone currently in group:{groupId} (live delete).
            long? groupId = null;
            try
            {
                if (tenant.FiscalYearId is > 0)
                {
                    groupId = await _groupRepository.GetGroupIdByChatIdAsync(
                        result.ChatId,
                        tenant.OrgId,
                        tenant.AppId,
                        tenant.FiscalYearId.Value,
                        cancellationToken);
                }
            }
            catch
            {
                groupId = null;
            }

            if (groupId is > 0)
            {
                await _hubContext.Clients
                    .Group(ChatHub.ChatGroup(groupId.Value))
                    .SendAsync("MessageDeleted", new
                    {
                        payload.messageId,
                        payload.chatId,
                        payload.senderUserId,
                        payload.receiverUserId,
                        payload.deletedBy,
                        payload.deletedAt,
                        payload.deleteFlag,
                        groupId = groupId.Value
                    }, cancellationToken);
            }
            else
            {
                var notifyUserIds = new HashSet<long> { result.SenderUserId, result.DeletedBy };
                if (result.ReceiverUserId is > 0)
                    notifyUserIds.Add(result.ReceiverUserId.Value);

                foreach (var notifyUserId in notifyUserIds)
                {
                    await _hubContext.Clients
                        .Group(ChatHub.UserGroup(notifyUserId))
                        .SendAsync("MessageDeleted", payload, cancellationToken);
                }
            }

            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Soft-clear all messages in a chat (delete_flag = 1). Chat stays; messages become invisible.
    /// </summary>
    [HttpPost("{chatId:int}/clear")]
    public async Task<IActionResult> ClearChat(
        int chatId,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            var result = await _chatService.ClearChatAsync(
                chatId, userId.Value, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, cancellationToken);

            var payload = new
            {
                chatId = result.ChatId,
                affectedCount = result.AffectedCount,
                peerUserId = result.PeerUserId,
                clearedBy = result.ClearedBy,
                clearedAt = result.ClearedAt
            };

            var notifyUserIds = new HashSet<long> { result.ClearedBy };
            if (result.PeerUserId is > 0)
                notifyUserIds.Add(result.PeerUserId.Value);

            foreach (var notifyUserId in notifyUserIds)
            {
                await _hubContext.Clients
                    .Group(ChatHub.UserGroup(notifyUserId))
                    .SendAsync("ChatCleared", payload, cancellationToken);
            }

            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Soft-delete all messages in a chat (delete_flag = 1). UI removes the chat from the list.
    /// Never physically deletes message rows.
    /// </summary>
    [HttpDelete("{chatId:int}")]
    public async Task<IActionResult> DeleteChat(
        int chatId,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            var result = await _chatService.DeleteChatAsync(
                chatId, userId.Value, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, cancellationToken);

            var payload = new
            {
                chatId = result.ChatId,
                affectedCount = result.AffectedCount,
                peerUserId = result.PeerUserId,
                deletedBy = result.DeletedBy,
                deletedAt = result.DeletedAt
            };

            var notifyUserIds = new HashSet<long> { result.DeletedBy };
            if (result.PeerUserId is > 0)
                notifyUserIds.Add(result.PeerUserId.Value);

            foreach (var notifyUserId in notifyUserIds)
            {
                await _hubContext.Clients
                    .Group(ChatHub.UserGroup(notifyUserId))
                    .SendAsync("ChatDeleted", payload, cancellationToken);
            }

            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Star or unstar a message for the authenticated user only
    /// (is_starred_by_sender OR is_starred_by_receiver on tab_messages).
    /// </summary>
    [HttpPut("messages/{messageId:long}/star")]
    [HttpPost("messages/{messageId:long}/star")]
    public async Task<IActionResult> ToggleMessageStar(
        long messageId,
        [FromBody] ToggleMessageStarRequest? request,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            if (request is null)
                return BadRequest(new { message = "Request body with isStarred is required." });

            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            var result = await _chatService.ToggleMessageStarAsync(
                messageId,
                userId.Value,
                request.IsStarred,
                tenant.OrgId,
                tenant.AppId,
                tenant.FiscalYearId,
                cancellationToken);

            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Set or clear the authenticated user's reaction on a message (tab_message_reactions).
    /// Pass null/empty reactionCode to remove.
    /// </summary>
    [HttpPut("messages/{messageId:long}/reaction")]
    [HttpPost("messages/{messageId:long}/reaction")]
    public async Task<IActionResult> ToggleMessageReaction(
        long messageId,
        [FromBody] ToggleMessageReactionRequest? request,
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            if (request is null)
                return BadRequest(new { message = "Request body is required." });

            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            var result = await _chatService.ToggleMessageReactionAsync(
                messageId,
                userId.Value,
                request.ReactionCode,
                tenant.OrgId,
                tenant.AppId,
                tenant.FiscalYearId,
                cancellationToken);

            // Live-sync reaction badge.
            // Group chats: resolve groupId from chatId (existing helper) — no DTO change.
            long? groupId = null;
            if (tenant.FiscalYearId is > 0)
            {
                groupId = await _groupRepository.GetGroupIdByChatIdAsync(
                    result.ChatId,
                    tenant.OrgId,
                    tenant.AppId,
                    tenant.FiscalYearId.Value,
                    cancellationToken);
            }

            var payload = new
            {
                messageId = result.MessageId,
                chatId = result.ChatId,
                reactorUserId = userId.Value,
                reactionCode = result.ReactionCode,
                reaction = result.Reaction,
                senderUserId = result.SenderUserId,
                receiverUserId = result.ReceiverUserId,
                groupId
            };

            if (groupId is > 0)
            {
                await _hubContext.Clients
                    .Group(ChatHub.ChatGroup(groupId.Value))
                    .SendAsync("MessageReaction", payload, cancellationToken);
            }
            else
            {
                var peerId =
                    result.SenderUserId == userId.Value
                        ? result.ReceiverUserId
                        : result.SenderUserId;

                if (peerId is > 0)
                {
                    await _hubContext.Clients
                        .Group(ChatHub.UserGroup(peerId.Value))
                        .SendAsync("MessageReaction", payload, cancellationToken);
                }

                await _hubContext.Clients
                    .Group(ChatHub.UserGroup(userId.Value))
                    .SendAsync("MessageReaction", payload, cancellationToken);
            }

            return Ok(result);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>
    /// All messages starred by the authenticated user across chats (delete_flag = 0 only).
    /// </summary>
    [HttpGet("messages/starred")]
    [HttpGet("starred-messages")]
    public async Task<IActionResult> GetStarredMessages(
        [FromQuery] int? orgId,
        [FromQuery] int? appId,
        [FromQuery] int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = CurrentUserHelper.GetUserId(User);
            if (userId is null)
                return Unauthorized(new { message = "Authenticated user id claim is missing." });

            var tenant = ResolveTenant(orgId, appId, fiscalYearId);
            var messages = await _chatService.GetStarredMessagesAsync(
                userId.Value, tenant.OrgId, tenant.AppId, tenant.FiscalYearId, cancellationToken);

            return Ok(messages);
        }
        catch (ChatOperationException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Group message: hub broadcast + per-member user hub + "New Message" notifications.
    /// </summary>
    private async Task NotifyGroupMessageAsync(
        SentMessageDto saved,
        object payload,
        long actorUserId,
        int orgId,
        int appId,
        int? fiscalYearId,
        CancellationToken cancellationToken)
    {
        await _hubContext.Clients
            .Group(ChatHub.ChatGroup(saved.GroupId!.Value))
            .SendAsync("ReceiveMessage", payload, cancellationToken);

        if (fiscalYearId is not > 0)
            return;

        try
        {
            var details = await _groupRepository.GetGroupDetailsAsync(
                saved.GroupId.Value,
                actorUserId,
                orgId,
                appId,
                fiscalYearId.Value,
                cancellationToken);

            var referenceType = $"GROUP:{saved.GroupId.Value}";
            var preview = string.IsNullOrWhiteSpace(saved.MessageBody)
                ? "New message"
                : saved.MessageBody;

            foreach (var member in details.Members)
            {
                if (!member.IsActive || member.UserId == saved.SenderUserId)
                    continue;

                // Personal hub so list/off-page clients get live message + green badge.
                await _hubContext.Clients
                    .Group(ChatHub.UserGroup(member.UserId))
                    .SendAsync("ReceiveMessage", payload, cancellationToken);

                // Online ⇒ delivered receipt + MessageDelivered for Message Info (WhatsApp-style).
                if (ChatHub.IsUserOnline(member.UserId))
                {
                    try
                    {
                        await _groupService.MarkMessageDeliveredAsync(
                            saved.MessageId,
                            member.UserId,
                            saved.ChatId,
                            orgId,
                            appId,
                            fiscalYearId,
                            cancellationToken);

                        await _hubContext.Clients
                            .Group(ChatHub.UserGroup(saved.SenderUserId))
                            .SendAsync(
                                "MessageDelivered",
                                new
                                {
                                    messageId = saved.MessageId,
                                    chatId = saved.ChatId,
                                    receiverUserId = member.UserId,
                                    senderUserId = saved.SenderUserId,
                                    groupId = saved.GroupId,
                                    deliveredAt = DateTimeOffset.UtcNow
                                },
                                cancellationToken);
                    }
                    catch (Exception)
                    {
                        // Delivery tracking must not fail the send.
                    }
                }

                try
                {
                    var notification = await _chatService.CreateMessageNotificationAsync(
                        member.UserId,
                        saved.SenderUserId,
                        preview,
                        saved.MessageId,
                        orgId,
                        appId,
                        fiscalYearId.Value,
                        referenceType,
                        cancellationToken);

                    var notificationPayload = new
                    {
                        notificationId = notification.NotificationId,
                        notificationType = notification.NotificationType,
                        title = notification.Title,
                        message = notification.Message,
                        referenceId = notification.ReferenceId,
                        referenceType = notification.ReferenceType ?? referenceType,
                        senderUserId = notification.SenderUserId,
                        createdDate = notification.CreatedDate,
                        isRead = notification.IsRead,
                        groupId = saved.GroupId
                    };

                    await _hubContext.Clients
                        .Group(ChatHub.UserGroup(member.UserId))
                        .SendAsync("ReceiveNotification", notificationPayload, cancellationToken);
                }
                catch (Exception)
                {
                    // Message already saved — do not fail send if one member's notification fails.
                }
            }
        }
        catch (Exception)
        {
            // Group hub already notified — member fan-out is best-effort.
        }
    }

    private static object BuildReceiveMessagePayload(SentMessageDto saved) =>
        new
        {
            messageId = saved.MessageId,
            chatId = saved.ChatId,
            senderUserId = saved.SenderUserId,
            messageBody = saved.MessageBody,
            messageTypeId = saved.MessageTypeId,
            sentAt = saved.SentAt,
            readAt = (DateTimeOffset?)null,
            receiverUserId = saved.ReceiverUserId,
            attachmentPath1 = saved.AttachmentPath1,
            attachmentPath2 = saved.AttachmentPath2,
            attachmentPath3 = saved.AttachmentPath3,
            attachmentPath4 = saved.AttachmentPath4,
            attachmentPath5 = saved.AttachmentPath5,
            parentMessageId = saved.ParentMessageId,
            forwardedFromMessageId = saved.ForwardedFromMessageId,
            forwardedBy = saved.ForwardedBy,
            groupId = saved.GroupId
        };

    private (int OrgId, int AppId, int? FiscalYearId) ResolveTenant(
        int? orgId,
        int? appId,
        int? fiscalYearId)
    {
        var resolvedOrg = orgId
            ?? CurrentUserHelper.GetIntClaim(User, "orgId", "org_id", "organizationId", "OrganisationId")
            ?? 0;

        var resolvedApp = appId
            ?? CurrentUserHelper.GetIntClaim(User, "appId", "app_id", "productId", "product_id")
            ?? _configuration.GetValue<int?>("SoftOnCloud:ProductId")
            ?? 0;

        var resolvedFy = fiscalYearId
            ?? CurrentUserHelper.GetIntClaim(User, "fiscalYearId", "fiscal_year_id");

        return (resolvedOrg, resolvedApp, resolvedFy);
    }
}
