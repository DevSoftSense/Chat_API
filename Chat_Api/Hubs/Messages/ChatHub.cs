using System.Collections.Concurrent;
using Chat.Application.Services.Interfaces.Groups;
using Chat.Application.Services.Interfaces.Organisation;
using Chat.Domain.Exceptions.Messages;
using Chat_Api.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;

namespace Chat_Api.Hubs.Messages;

/// <summary>
/// Chat + in-memory presence (Online/Offline). No database for presence.
/// Also relays delivery acks on the same hub (no second connection).
/// Group Chat uses additive SignalR groups: group:{groupId}.
/// All User Communication observers: comm-observers:{orgId}:{appId}.
/// </summary>
[Authorize]
public sealed class ChatHub : Hub
{
    private readonly IGroupService _groupService;
    private readonly IOrganisationSettingService _organisationSettingService;
    private readonly ICommObserverAccessService _commObserverAccess;
    private readonly IConfiguration _configuration;

    /// <summary>connectionId → observer SignalR group name (for disconnect cleanup).</summary>
    private static readonly ConcurrentDictionary<string, string> ObserverGroupByConnection = new();

    public ChatHub(
        IGroupService groupService,
        IOrganisationSettingService organisationSettingService,
        ICommObserverAccessService commObserverAccess,
        IConfiguration configuration)
    {
        _groupService = groupService;
        _organisationSettingService = organisationSettingService;
        _commObserverAccess = commObserverAccess;
        _configuration = configuration;
    }

    public static string UserGroup(long userId) => $"user:{userId}";

    /// <summary>SignalR group name for Group Chat realtime delivery.</summary>
    public static string ChatGroup(long groupId) => $"group:{groupId}";

    /// <summary>All User Communication observers (PRIME/ADMIN per org setting).</summary>
    public static string CommObserversGroup(int orgId, int appId) =>
        $"comm-observers:{orgId}:{appId}";

    /// <summary>userId → active SignalR connection count (multi-tab safe).</summary>
    private static readonly ConcurrentDictionary<long, int> OnlineConnectionCounts = new();

    /// <summary>userId → Settings My Status (Online/Away/Busy/Offline) while connected.</summary>
    private static readonly ConcurrentDictionary<long, (string Status, string StatusMessage)> UserStatuses = new();

    private static readonly HashSet<string> AllowedPresenceStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Online", "Away", "Busy", "Offline"
    };

    public static bool IsUserOnline(long userId) =>
        OnlineConnectionCounts.TryGetValue(userId, out var count) && count > 0;

    /// <summary>Snapshot of currently online SoftOnCloud user ids (in-memory presence).</summary>
    public static IReadOnlyList<long> GetOnlineUserIds() =>
        OnlineConnectionCounts
            .Where(kv => kv.Value > 0)
            .Select(kv => kv.Key)
            .ToArray();

    public override async Task OnConnectedAsync()
    {
        var userId = CurrentUserHelper.GetUserId(Context.User);
        if (userId is null)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId.Value));

        var count = OnlineConnectionCounts.AddOrUpdate(userId.Value, 1, (_, prev) => prev + 1);

        if (count == 1)
            await Clients.Others.SendAsync("UserOnline", userId.Value);

        var onlineIds = OnlineConnectionCounts
            .Where(kv => kv.Value > 0)
            .Select(kv => kv.Key)
            .ToArray();
        await Clients.Caller.SendAsync("OnlineUsers", onlineIds);

        // Late snapshot of peer statuses for this caller.
        await Clients.Caller.SendAsync("UserStatuses", SnapshotUserStatuses());

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Late subscribers (UI mounted after hub already connected) request a fresh snapshot.
    /// </summary>
    public Task RequestOnlineUsers()
    {
        var onlineIds = OnlineConnectionCounts
            .Where(kv => kv.Value > 0)
            .Select(kv => kv.Key)
            .ToArray();
        return Clients.Caller.SendAsync("OnlineUsers", onlineIds);
    }

    /// <summary>Settings → My Status snapshot for late subscribers.</summary>
    public Task RequestUserStatuses() =>
        Clients.Caller.SendAsync("UserStatuses", SnapshotUserStatuses());

    /// <summary>
    /// Settings → My Status. Broadcasts UserStatusChanged to other clients.
    /// </summary>
    public async Task SetUserStatus(string status, string? statusMessage)
    {
        var userId = CurrentUserHelper.GetUserId(Context.User)
            ?? throw new HubException("Authenticated user id claim is missing.");

        var normalized = string.IsNullOrWhiteSpace(status) ? "Online" : status.Trim();
        if (!AllowedPresenceStatuses.Contains(normalized))
            throw new HubException("status must be Online, Away, Busy, or Offline.");

        normalized = AllowedPresenceStatuses.First(s =>
            s.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        var message = (statusMessage ?? string.Empty).Trim();
        if (message.Length > 200)
            message = message[..200];

        UserStatuses[userId] = (normalized, message);

        await Clients.Others.SendAsync(
            "UserStatusChanged",
            new
            {
                userId,
                status = normalized,
                statusMessage = message
            });
    }

    /// <summary>
    /// 1:1 typing indicator — notify peer only (WhatsApp-style).
    /// </summary>
    public async Task SendTyping(long peerUserId, bool isTyping)
    {
        var userId = CurrentUserHelper.GetUserId(Context.User)
            ?? throw new HubException("Authenticated user id claim is missing.");

        if (peerUserId <= 0 || peerUserId == userId)
            return;

        await Clients.Group(UserGroup(peerUserId)).SendAsync(
            "UserTyping",
            new
            {
                userId,
                peerUserId,
                isTyping
            });
    }

    /// <summary>
    /// Group typing indicator — notify other members in group:{groupId}.
    /// </summary>
    public async Task SendGroupTyping(long groupId, bool isTyping)
    {
        var userId = CurrentUserHelper.GetUserId(Context.User)
            ?? throw new HubException("Authenticated user id claim is missing.");

        if (groupId <= 0)
            return;

        // Broadcast to other connections — UI filters by groupId.
        // Avoids depending on JoinGroupChat / fiscalYearId for typing.
        await Clients.Others.SendAsync(
            "GroupTyping",
            new
            {
                userId,
                groupId,
                isTyping
            });
    }

    private static object[] SnapshotUserStatuses() =>
        UserStatuses
            .Select(kv => (object)new
            {
                userId = kv.Key,
                status = kv.Value.Status,
                statusMessage = kv.Value.StatusMessage
            })
            .ToArray();

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (ObserverGroupByConnection.TryRemove(Context.ConnectionId, out var observerGroup))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, observerGroup);

        var userId = CurrentUserHelper.GetUserId(Context.User);
        if (userId is not null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, UserGroup(userId.Value));

            var remaining = OnlineConnectionCounts.AddOrUpdate(
                userId.Value,
                0,
                (_, prev) => Math.Max(0, prev - 1));

            if (remaining == 0)
            {
                OnlineConnectionCounts.TryRemove(userId.Value, out _);
                UserStatuses.TryRemove(userId.Value, out _);
                await Clients.Others.SendAsync("UserOffline", userId.Value);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// All User Communication page: join org observer SignalR room (JWT userType must match setting).
    /// Does not change Groups membership — live ReceiveMessage / MessageDeleted only.
    /// </summary>
    public async Task JoinCommObserverWatch(int? orgId = null, int? appId = null)
    {
        var userId = CurrentUserHelper.GetUserId(Context.User)
            ?? throw new HubException("Authenticated user id claim is missing.");

        var tenant = ResolveTenant(orgId, appId, null);
        if (tenant.OrgId <= 0 || tenant.AppId <= 0)
            throw new HubException("orgId and appId are required.");

        var requiredTypes = await _organisationSettingService.GetCommFromUserTypesAsync(
            tenant.OrgId, tenant.AppId);

        if (!await _commObserverAccess.IsObserverAsync(
                Context.User, requiredTypes, tenant.OrgId, tenant.AppId))
            throw new HubException(
                "Not authorized for All User Communication observer watch.");

        var groupName = CommObserversGroup(tenant.OrgId, tenant.AppId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        ObserverGroupByConnection[Context.ConnectionId] = groupName;
    }

    public async Task LeaveCommObserverWatch()
    {
        if (!ObserverGroupByConnection.TryRemove(Context.ConnectionId, out var groupName))
            return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }

    /// <summary>Client joins group:{groupId} after membership is verified.</summary>
    public async Task JoinGroupChat(
        long groupId,
        int? orgId = null,
        int? appId = null,
        int? fiscalYearId = null)
    {
        var userId = CurrentUserHelper.GetUserId(Context.User)
            ?? throw new HubException("Authenticated user id claim is missing.");

        if (groupId <= 0)
            throw new HubException("groupId is required.");

        var tenant = ResolveTenant(orgId, appId, fiscalYearId);

        // When fiscal year is missing from JWT/session, still join SignalR group
        // so live typing / messages work; skip DB membership assert.
        if (tenant.FiscalYearId is null or <= 0)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, ChatGroup(groupId));
            return;
        }

        try
        {
            await _groupService.AssertGroupMemberAsync(
                groupId, userId, tenant.OrgId, tenant.AppId, tenant.FiscalYearId.Value);
        }
        catch (ChatOperationException ex)
        {
            throw new HubException(ex.Message);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, ChatGroup(groupId));
    }

    public async Task LeaveGroupChat(long groupId)
    {
        if (groupId <= 0) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ChatGroup(groupId));
    }

    /// <summary>
    /// Receiver acks a message as delivered. Persists for group Message Info, then notifies sender.
    /// </summary>
    public async Task AckMessageDelivered(long messageId, long senderUserId, int chatId)
    {
        var receiverUserId = CurrentUserHelper.GetUserId(Context.User)
            ?? throw new HubException("Authenticated user id claim is missing.");

        if (messageId <= 0 || senderUserId <= 0 || chatId <= 0)
            throw new HubException("messageId, senderUserId and chatId are required.");

        if (senderUserId == receiverUserId)
            return;

        var tenant = ResolveTenant(null, null, null);
        try
        {
            // Repository loads org/app/fy from the message row when claims are incomplete.
            await _groupService.MarkMessageDeliveredAsync(
                messageId,
                receiverUserId,
                chatId,
                tenant.OrgId,
                tenant.AppId,
                tenant.FiscalYearId);
        }
        catch
        {
            // Persist is best-effort — still notify sender for live UI.
        }

        await Clients.Group(UserGroup(senderUserId)).SendAsync(
            "MessageDelivered",
            new
            {
                messageId,
                chatId,
                receiverUserId,
                senderUserId,
                deliveredAt = DateTimeOffset.UtcNow
            });
    }

    private (int OrgId, int AppId, int? FiscalYearId) ResolveTenant(
        int? orgId,
        int? appId,
        int? fiscalYearId)
    {
        var resolvedOrg = orgId
            ?? CurrentUserHelper.GetIntClaim(Context.User, "orgId", "org_id", "organizationId", "OrganisationId")
            ?? 0;

        var resolvedApp = appId
            ?? CurrentUserHelper.GetIntClaim(Context.User, "appId", "app_id", "productId", "product_id")
            ?? _configuration.GetValue<int?>("SoftOnCloud:ProductId")
            ?? 0;

        var resolvedFy = fiscalYearId
            ?? CurrentUserHelper.GetIntClaim(Context.User, "fiscalYearId", "fiscal_year_id");

        return (resolvedOrg, resolvedApp, resolvedFy);
    }
}
