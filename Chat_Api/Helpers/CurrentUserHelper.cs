using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Chat_Api.Helpers;

public static class CurrentUserHelper
{
    public static long? GetUserId(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return null;

        string[] claimNames =
        [
            "userId",
            "userid",
            "user_id",
            ClaimTypes.NameIdentifier,
            "sub",
            "uid"
        ];

        foreach (var name in claimNames)
        {
            var value = user.FindFirst(name)?.Value;
            if (long.TryParse(value, out var userId) && userId > 0)
                return userId;
        }

        return null;
    }

    public static int? GetIntClaim(ClaimsPrincipal? user, params string[] claimNames)
    {
        if (user is null)
            return null;

        foreach (var name in claimNames)
        {
            var value = user.FindFirst(name)?.Value;
            if (int.TryParse(value, out var parsed) && parsed > 0)
                return parsed;
        }

        return null;
    }

    public static string NormalizeUserType(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    /// <summary>
    /// Map SoftOnCloud roleNames/roleCodes to PRIME|ADMIN|SUB.
    /// "Admin" / "CHATS_ADMIN" → ADMIN (userType may still be SUB).
    /// </summary>
    public static string NormalizeCommRoleToken(string? value)
    {
        var normalized = NormalizeUserType(value)?.Replace(' ', '_').Replace('-', '_');
        if (string.IsNullOrEmpty(normalized))
            return string.Empty;

        if (normalized is "ADMIN" or "PRIME" or "SUB")
            return normalized;

        if (normalized.Contains("ADMIN", StringComparison.Ordinal) ||
            normalized is "CHATS_ADMIN" ||
            normalized.EndsWith("_ADMIN", StringComparison.Ordinal))
            return "ADMIN";

        if (normalized.Contains("PRIME", StringComparison.Ordinal))
            return "PRIME";

        return normalized;
    }

    public static string? GetIdentityType(ClaimsPrincipal? user)
    {
        if (user is null)
            return null;

        foreach (var name in new[]
        {
            "identityType", "IdentityType", "userType", "UserType", "identity_type", "user_type"
        })
        {
            var value = user.FindFirst(name)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return NormalizeUserType(value);
        }

        return null;
    }

    /// <summary>
    /// Logged-in SoftOnCloud userType must match the required type from DB setting
    /// (exclusive: PRIME-only or ADMIN-only depending on setting value).
    /// </summary>
    public static bool IsCommFromEligibleUserType(
        string? userType,
        IReadOnlyList<string>? requiredUserTypes)
    {
        var type = NormalizeUserType(userType);
        if (string.IsNullOrEmpty(type))
            return false;

        var allow = new HashSet<string>(StringComparer.Ordinal);
        if (requiredUserTypes is not null)
        {
            foreach (var item in requiredUserTypes)
            {
                var normalized = NormalizeUserType(item);
                if (!string.IsNullOrEmpty(normalized))
                    allow.Add(normalized);
            }
        }

        if (allow.Count == 0)
            allow.Add("PRIME");

        return allow.Contains(type);
    }

    public static bool IsCommObserver(
        ClaimsPrincipal? user,
        IReadOnlyList<string>? requiredUserTypes)
    {
        if (user is null)
            return false;

        if (IsCommFromEligibleUserType(GetIdentityType(user), requiredUserTypes))
            return true;

        foreach (var name in new[]
                 {
                     "roleNames", "RoleNames", "role", "roles", "roleCodes", "RoleCodes",
                     ClaimTypes.Role
                 })
        {
            foreach (var claim in user.FindAll(name))
            {
                foreach (var part in ExpandClaimValues(claim.Value))
                {
                    var token = NormalizeCommRoleToken(part);
                    if (IsCommFromEligibleUserType(token, requiredUserTypes))
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>SoftOnCloud role codes from JWT (e.g. CHATS_ADMIN).</summary>
    public static IReadOnlyList<string> GetRoleCodes(ClaimsPrincipal? user)
    {
        if (user is null)
            return Array.Empty<string>();

        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "roleCodes", "RoleCodes", "role_code", "roleCode", ClaimTypes.Role })
        {
            foreach (var claim in user.FindAll(name))
            {
                foreach (var part in ExpandClaimValues(claim.Value))
                {
                    var cleaned = part?.Trim();
                    if (!string.IsNullOrWhiteSpace(cleaned))
                        codes.Add(cleaned);
                }
            }
        }

        return codes.ToList();
    }

    /// <summary>Supports "Admin", "Admin,Prime", and JSON ["Admin"].</summary>
    private static IEnumerable<string> ExpandClaimValues(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            yield break;

        var value = raw.Trim();
        if (value.StartsWith('[') && value.EndsWith(']'))
        {
            value = value.Trim('[', ']');
            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var cleaned = part.Trim().Trim('"').Trim('\'');
                if (!string.IsNullOrWhiteSpace(cleaned))
                    yield return cleaned;
            }

            yield break;
        }

        foreach (var part in value.Split(
                     [',', ';', '|'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return part;
    }
}

/// <summary>
/// All User Communication access: JWT userType/roles first, then SoftOnCloud
/// product-users roleNames (e.g. userType=sub + roleNames=Admin).
/// (Merged here — no separate new file.)
/// </summary>
public interface ICommObserverAccessService
{
    Task<bool> IsObserverAsync(
        ClaimsPrincipal? user,
        IReadOnlyList<string> requiredUserTypes,
        int orgId,
        int appId,
        CancellationToken cancellationToken = default);
}

public sealed class CommObserverAccessService : ICommObserverAccessService
{
    private static readonly ConcurrentDictionary<string, (bool Allowed, long ExpiresAtMs)> Cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(45);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CommObserverAccessService> _logger;

    public CommObserverAccessService(
        IHttpClientFactory httpClientFactory,
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration,
        ILogger<CommObserverAccessService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> IsObserverAsync(
        ClaimsPrincipal? user,
        IReadOnlyList<string> requiredUserTypes,
        int orgId,
        int appId,
        CancellationToken cancellationToken = default)
    {
        if (CurrentUserHelper.IsCommObserver(user, requiredUserTypes))
            return true;

        var userId = CurrentUserHelper.GetUserId(user);
        if (userId is null or <= 0 || orgId <= 0 || appId <= 0)
            return false;

        var cacheKey =
            $"{orgId}:{appId}:{userId.Value}:{string.Join(',', requiredUserTypes ?? Array.Empty<string>())}";
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (Cache.TryGetValue(cacheKey, out var hit) && hit.ExpiresAtMs > nowMs)
            return hit.Allowed;

        var (allowed, cacheable) = await ResolveFromSoftOnCloudAsync(
            userId.Value,
            orgId,
            appId,
            requiredUserTypes ?? Array.Empty<string>(),
            cancellationToken);

        // Do not cache infra misses (no bearer on SignalR, SoftOnCloud down) —
        // those were falsely locking observers out for CacheTtl.
        if (cacheable)
        {
            Cache[cacheKey] = (
                allowed,
                DateTimeOffset.UtcNow.Add(CacheTtl).ToUnixTimeMilliseconds());
        }

        return allowed;
    }

    /// <summary>
    /// REST sends Authorization: Bearer. SignalR browsers send JWT as ?access_token=
    /// (see Program.cs JwtBearerEvents) — hub invokes have no Authorization header.
    /// </summary>
    private static string? ResolveBearerAuthorization(HttpContext? http)
    {
        if (http is null)
            return null;

        var header = http.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(header) &&
            header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return header;

        var accessToken = http.Request.Query["access_token"].ToString();
        if (!string.IsNullOrWhiteSpace(accessToken))
            return $"Bearer {accessToken}";

        return null;
    }

    private async Task<(bool Allowed, bool Cacheable)> ResolveFromSoftOnCloudAsync(
        long userId,
        int orgId,
        int appId,
        IReadOnlyList<string> requiredUserTypes,
        CancellationToken cancellationToken)
    {
        var baseUrl = (_configuration["SoftOnCloud:ApiBaseUrl"] ?? "").TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
            return (false, false);

        var bearer = ResolveBearerAuthorization(_httpContextAccessor.HttpContext);
        if (string.IsNullOrWhiteSpace(bearer))
        {
            _logger.LogWarning(
                "AUC SoftOnCloud observer lookup skipped: no Bearer/access_token on request (org={OrgId} app={AppId})",
                orgId, appId);
            return (false, false);
        }

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(CommObserverAccessService));
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{baseUrl}/api/organisations/{orgId}/products/{appId}/users");
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(bearer);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "SoftOnCloud product-users for AUC observer returned {Status}",
                    (int)response.StatusCode);
                return (false, false);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = doc.RootElement;
            var users = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray()
                : root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                    ? data.EnumerateArray()
                    : root.TryGetProperty("users", out var usersProp) && usersProp.ValueKind == JsonValueKind.Array
                        ? usersProp.EnumerateArray()
                        : root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
                            ? items.EnumerateArray()
                            : Enumerable.Empty<JsonElement>();

            foreach (var row in users)
            {
                var rowUserId = ReadLong(row, "userId", "UserId");
                if (rowUserId != userId)
                    continue;

                foreach (var token in CollectEligibilityTokens(row))
                {
                    if (CurrentUserHelper.IsCommFromEligibleUserType(token, requiredUserTypes))
                        return (true, true);
                }

                return (false, true);
            }

            // User not in product-users list — definitive for this token/org/app.
            return (false, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SoftOnCloud product-users lookup failed for AUC observer");
            return (false, false);
        }
    }

    private static IEnumerable<string> CollectEligibilityTokens(JsonElement row)
    {
        var userType = ReadString(row, "userType", "UserType", "identityType", "IdentityType");
        if (!string.IsNullOrWhiteSpace(userType))
            yield return CurrentUserHelper.NormalizeCommRoleToken(userType);

        foreach (var name in ReadStringArray(row, "roleNames", "RoleNames"))
        {
            var token = CurrentUserHelper.NormalizeCommRoleToken(name);
            if (!string.IsNullOrEmpty(token))
                yield return token;
        }

        foreach (var code in ReadStringArray(row, "roleCodes", "RoleCodes"))
        {
            var token = CurrentUserHelper.NormalizeCommRoleToken(code);
            if (!string.IsNullOrEmpty(token))
                yield return token;
        }
    }

    private static long ReadLong(JsonElement row, params string[] names)
    {
        foreach (var name in names)
        {
            if (!row.TryGetProperty(name, out var prop))
                continue;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var n))
                return n;
            if (prop.ValueKind == JsonValueKind.String &&
                long.TryParse(prop.GetString(), out var parsed))
                return parsed;
        }

        return 0;
    }

    private static string? ReadString(JsonElement row, params string[] names)
    {
        foreach (var name in names)
        {
            if (!row.TryGetProperty(name, out var prop))
                continue;
            if (prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }

        return null;
    }

    private static IEnumerable<string> ReadStringArray(JsonElement row, params string[] names)
    {
        foreach (var name in names)
        {
            if (!row.TryGetProperty(name, out var prop))
                continue;

            if (prop.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in prop.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var s = item.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                            yield return s!;
                    }
                }

                yield break;
            }

            if (prop.ValueKind == JsonValueKind.String)
            {
                var raw = prop.GetString() ?? "";
                foreach (var part in raw.Split(
                             [',', ';', '|'],
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    yield return part;
                yield break;
            }
        }
    }
}
