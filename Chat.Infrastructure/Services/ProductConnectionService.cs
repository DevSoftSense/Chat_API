using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Chat.Infrastructure.Services;

/// <summary>
/// Calls the existing SoftOnCloud Product Connection API and returns resolvedConnectionString.
/// Does not create or modify SoftOnCloud APIs.
/// </summary>
public sealed class ProductConnectionService : IProductConnectionService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProductConnectionService> _logger;

    public ProductConnectionService(
        IHttpClientFactory httpClientFactory,
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration,
        ILogger<ProductConnectionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> GetResolvedConnectionStringAsync(CancellationToken cancellationToken = default)
    {
        var useApi = _configuration.GetValue("SoftOnCloud:UseProductConnectionApi", true);
        var localFallback = _configuration.GetConnectionString("DefaultConnection");

        if (!useApi)
        {
            if (!string.IsNullOrWhiteSpace(localFallback))
                return localFallback;

            throw new InvalidOperationException(
                "SoftOnCloud:UseProductConnectionApi is false and ConnectionStrings:DefaultConnection is not configured.");
        }

        var productId = _configuration.GetValue<int?>("SoftOnCloud:ProductId");
        if (productId is null or <= 0)
        {
            if (!string.IsNullOrWhiteSpace(localFallback))
            {
                _logger.LogWarning("SoftOnCloud:ProductId is not configured; using DefaultConnection fallback.");
                return localFallback;
            }

            throw new InvalidOperationException("SoftOnCloud:ProductId is not configured.");
        }

        var bearerToken = GetBearerTokenFromRequest();
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            if (!string.IsNullOrWhiteSpace(localFallback))
            {
                _logger.LogWarning("No JWT on the request; using DefaultConnection fallback.");
                return localFallback;
            }

            throw new InvalidOperationException(
                "Authorization Bearer JWT is required to resolve the product transaction connection.");
        }

        var baseUrl = (_configuration["SoftOnCloud:ApiBaseUrl"] ?? "https://api.softoncloud.com").TrimEnd('/');
        var requestUri = $"{baseUrl}/api/auth/product-connection?productId={productId.Value}";

        var client = _httpClientFactory.CreateClient(nameof(ProductConnectionService));
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Product Connection API failed. Status={StatusCode} Body={Body}",
                (int)response.StatusCode,
                body);

            if (!string.IsNullOrWhiteSpace(localFallback) &&
                _configuration.GetValue("SoftOnCloud:AllowDefaultConnectionFallback", false))
            {
                _logger.LogWarning("Falling back to DefaultConnection after Product Connection API failure.");
                return localFallback;
            }

            throw new InvalidOperationException(
                $"Product Connection API returned {(int)response.StatusCode}: {body}");
        }

        var payload = await response.Content.ReadFromJsonAsync<ProductConnectionResponse>(
            cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(payload?.ResolvedConnectionString))
        {
            throw new InvalidOperationException(
                "Product Connection API response did not include resolvedConnectionString.");
        }

        return payload.ResolvedConnectionString;
    }

    private string? GetBearerTokenFromRequest()
    {
        var header = _httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header))
            return null;

        const string prefix = "Bearer ";
        if (header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return header[prefix.Length..].Trim();

        return header.Trim();
    }

    private sealed class ProductConnectionResponse
    {
        [JsonPropertyName("productId")]
        public int ProductId { get; set; }

        [JsonPropertyName("productCode")]
        public string? ProductCode { get; set; }

        [JsonPropertyName("productName")]
        public string? ProductName { get; set; }

        [JsonPropertyName("dbGroup")]
        public string? DbGroup { get; set; }

        [JsonPropertyName("resolvedConnectionString")]
        public string? ResolvedConnectionString { get; set; }

        [JsonPropertyName("fromCache")]
        public bool FromCache { get; set; }
    }
}
