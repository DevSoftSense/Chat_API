using System.Collections.Concurrent;
using Chat.Application.Services.Interfaces.Groups;
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
/// </summary>
[Authorize]
public sealed class ChatHub : Hub
{
    private readonly IGroupService _groupService;
    private readonly IConfiguration _configuration;

    public ChatHub(
        IGroupService groupService,
        IConfiguration configuration)
    {
        _groupService = groupService;
        _configuration = configuration;
    }

    public static string UserGroup(long userId) => $"user:{userId}";

    /// <summary>SignalR group name for Group Chat realtime delivery.</summary>
    public static string ChatGroup(long groupId) => $"group:{groupId}";

    /// <summary>userId → active SignalR connection count (multi-tab safe).</summary>
    private static readonly ConcurrentDictionary<long, int> OnlineConnectionCounts = new();

    public static bool IsUserOnline(long userId) =>
        OnlineConnectionCounts.TryGetValue(userId, out var count) && count > 0;

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

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
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
                await Clients.Others.SendAsync("UserOffline", userId.Value);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Client joins group:{groupId} after membership is verified.</summary>
    public async Task JoinGroupChat(long groupId)
    {
        var userId = CurrentUserHelper.GetUserId(Context.User)
            ?? throw new HubException("Authenticated user id claim is missing.");

        if (groupId <= 0)
            throw new HubException("groupId is required.");

        var tenant = ResolveTenant(null, null, null);
        if (tenant.FiscalYearId is null or <= 0)
            throw new HubException("fiscalYearId is required.");

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
