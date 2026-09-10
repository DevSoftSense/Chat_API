namespace Chat.Infrastructure.Services;

/// <summary>
/// Resolves the tenant Transaction DB connection string via SoftOnCloud
/// GET /api/auth/product-connection (existing API — call only from backend).
/// </summary>
public interface IProductConnectionService
{
    /// <summary>
    /// Returns <c>resolvedConnectionString</c> for the configured product.
    /// Uses the current request JWT as <c>Authorization: Bearer</c>.
    /// Falls back to <c>ConnectionStrings:DefaultConnection</c> when configured for local use.
    /// </summary>
    Task<string> GetResolvedConnectionStringAsync(CancellationToken cancellationToken = default);
}
