using System.Security.Claims;

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
}
