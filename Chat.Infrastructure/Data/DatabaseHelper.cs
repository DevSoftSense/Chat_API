using Chat.Infrastructure.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Chat.Infrastructure.Data;

/// <summary>
/// Opens Npgsql connections for Master (product) and Default (tenant/chat) databases.
/// Tenant/default connections are resolved via SoftOnCloud Product Connection API when enabled.
/// </summary>
public sealed class DatabaseHelper
{
    private readonly IConfiguration _configuration;
    private readonly IProductConnectionService _productConnectionService;
    private readonly ILogger<DatabaseHelper> _logger;

    public DatabaseHelper(
        IConfiguration configuration,
        IProductConnectionService productConnectionService,
        ILogger<DatabaseHelper> logger)
    {
        _configuration = configuration;
        _productConnectionService = productConnectionService;
        _logger = logger;
    }

    public NpgsqlConnection GetMasterConnection()
    {
        var connectionString = _configuration.GetConnectionString("MasterConnection")
            ?? throw new InvalidOperationException("Connection string 'MasterConnection' is not configured.");

        return new NpgsqlConnection(connectionString);
    }

    /// <summary>
    /// Synchronous open using local DefaultConnection only (no SoftOnCloud call).
    /// Prefer <see cref="GetDefaultConnectionAsync"/> for tenant Transaction DB access.
    /// </summary>
    public NpgsqlConnection GetDefaultConnection()
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is not configured. " +
                "Use GetDefaultConnectionAsync() to resolve via Product Connection API.");

        return new NpgsqlConnection(connectionString);
    }

    /// <summary>
    /// Opens the tenant Transaction DB using resolvedConnectionString from
    /// GET /api/auth/product-connection?productId={id} (Bearer JWT from the current request).
    /// </summary>
    public async Task<NpgsqlConnection> GetDefaultConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = await _productConnectionService
            .GetResolvedConnectionStringAsync(cancellationToken)
            .ConfigureAwait(false);

        return new NpgsqlConnection(connectionString);
    }

    /// <summary>
    /// Npgsql command timeout in seconds. Config: Database:CommandTimeoutSeconds (default 30).
    /// </summary>
    public int CommandTimeoutSeconds =>
        Math.Clamp(_configuration.GetValue("Database:CommandTimeoutSeconds", 30), 5, 120);

    public async Task<List<T>> ExecuteRawQueryAsync<T>(
        string sql,
        Action<NpgsqlCommand> buildParameters,
        Func<NpgsqlDataReader, T> map,
        CancellationToken cancellationToken = default)
    {
        var results = new List<T>();

        try
        {
            await using var connection = await GetDefaultConnectionAsync(cancellationToken).ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(sql, connection)
            {
                CommandTimeout = CommandTimeoutSeconds
            };

            buildParameters(command);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                results.Add(map(reader));
            }

            return results;
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error executing query. SqlState={SqlState}", ex.SqlState);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error executing raw query");
            throw;
        }
    }

    public async Task ExecuteNonQueryAsync(
        string sql,
        Action<NpgsqlCommand> buildParameters,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await GetDefaultConnectionAsync(cancellationToken).ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(sql, connection)
            {
                CommandTimeout = CommandTimeoutSeconds
            };

            buildParameters(command);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error executing non-query. SqlState={SqlState}", ex.SqlState);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error executing non-query");
            throw;
        }
    }

    public async Task<T?> ExecuteScalarAsync<T>(
        string sql,
        Action<NpgsqlCommand> buildParameters,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await GetDefaultConnectionAsync(cancellationToken).ConfigureAwait(false);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = new NpgsqlCommand(sql, connection)
            {
                CommandTimeout = CommandTimeoutSeconds
            };

            buildParameters(command);

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result is null or DBNull)
                return default;

            return (T)Convert.ChangeType(result, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
        }
        catch (PostgresException ex)
        {
            _logger.LogError(ex, "PostgreSQL error executing scalar. SqlState={SqlState}", ex.SqlState);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error executing scalar");
            throw;
        }
    }
}
