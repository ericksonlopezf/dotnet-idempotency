// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using MySqlConnector;

namespace EricksonLopez.Idempotency.MySql;

/// <summary>
/// Provides a MySQL persistence store implementation for <see cref="IIdempotencyStore"/> using Dapper and <see cref="MySqlDataSource"/>.
/// </summary>
public sealed class MySqlIdempotencyStore : IIdempotencyStore
{
    private readonly MySqlDataSource _dataSource;

    private const string InsertCommandSql = """
        INSERT IGNORE INTO idempotency_records (
            id, tenant_id, scope, idempotency_key, fingerprint, status,
            owner_token, concurrency_version, created_at_utc, lease_expires_at_utc, retention_expires_at_utc
        )
        VALUES (
            @Id, @TenantId, @Scope, @Key, @Fingerprint, 1,
            @OwnerToken, 1, @Now, @LeaseExpiresAt, @RetentionExpiresAt
        );
        """;

    private const string SelectExistingSql = """
        SELECT id AS Id,
               tenant_id AS TenantId,
               scope AS Scope,
               idempotency_key AS IdempotencyKey,
               fingerprint AS Fingerprint,
               status AS Status,
               owner_token AS OwnerToken,
               concurrency_version AS ConcurrencyVersion,
               response_status_code AS ResponseStatusCode,
               response_headers AS ResponseHeaders,
               response_body AS ResponseBody,
               created_at_utc AS CreatedAtUtc,
               lease_expires_at_utc AS LeaseExpiresAtUtc,
               completed_at_utc AS CompletedAtUtc,
               retention_expires_at_utc AS RetentionExpiresAtUtc
        FROM idempotency_records
        WHERE tenant_id = @TenantId AND scope = @Scope AND idempotency_key = @Key;
        """;

    private const string StealLeaseSql = """
        UPDATE idempotency_records
        SET owner_token = @NewOwnerToken,
            concurrency_version = concurrency_version + 1,
            status = 1,
            fingerprint = @Fingerprint,
            lease_expires_at_utc = @NewLeaseExpiresAt,
            created_at_utc = @Now
        WHERE tenant_id = @TenantId
          AND scope = @Scope
          AND idempotency_key = @Key
          AND (
              (status = 1 AND lease_expires_at_utc < @Now)
              OR status = 3
          );
        """;

    private const string MarkCompletedSql = """
        UPDATE idempotency_records
        SET status = 2,
            response_status_code = @StatusCode,
            response_headers = @Headers,
            response_body = @ResponseBody,
            completed_at_utc = @Now,
            retention_expires_at_utc = @RetentionExpiresAt
        WHERE tenant_id = @TenantId
          AND scope = @Scope
          AND idempotency_key = @Key
          AND owner_token = @OwnerToken
          AND concurrency_version = @ConcurrencyVersion;
        """;

    private const string MarkFailedSql = """
        UPDATE idempotency_records
        SET status = 3,
            completed_at_utc = @Now
        WHERE tenant_id = @TenantId
          AND scope = @Scope
          AND idempotency_key = @Key
          AND owner_token = @OwnerToken
          AND concurrency_version = @ConcurrencyVersion;
        """;

    private const string CleanupBatchSql = """
        DELETE FROM idempotency_records
        WHERE retention_expires_at_utc < @UtcNow
        LIMIT @BatchSize;
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlIdempotencyStore"/> class with the specified MySQL data source.
    /// </summary>
    /// <param name="dataSource">The configured MySQL data source.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dataSource"/> is <see langword="null"/></exception>
    public MySqlIdempotencyStore(MySqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    /// <inheritdoc />
    public async Task<IdempotencyClaimResult> TryAcquireAsync(
        Guid tenantId,
        string scope,
        IdempotencyKey key,
        string fingerprint,
        TimeSpan leaseDuration,
        TimeSpan retentionDuration,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await TryAcquireCoreAsync(
            connection, null, tenantId, scope, key, fingerprint, leaseDuration, retentionDuration, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<IdempotencyClaimResult> TryAcquireCoreAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        Guid tenantId,
        string scope,
        IdempotencyKey key,
        string fingerprint,
        TimeSpan leaseDuration,
        TimeSpan retentionDuration,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var leaseExpiresAt = now.Add(leaseDuration);
        var retentionExpiresAt = now.Add(retentionDuration);
        var ownerToken = Guid.NewGuid();
        var recordId = Guid.NewGuid();

        var insertParams = new
        {
            Id = recordId.ToString(),
            TenantId = tenantId.ToString(),
            Scope = scope,
            Key = key.Value,
            Fingerprint = fingerprint,
            OwnerToken = ownerToken.ToString(),
            Now = now.UtcDateTime,
            LeaseExpiresAt = leaseExpiresAt.UtcDateTime,
            RetentionExpiresAt = retentionExpiresAt.UtcDateTime
        };

        var rowsInserted = await connection.ExecuteAsync(
            new CommandDefinition(InsertCommandSql, insertParams, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (rowsInserted > 0)
        {
            return new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, ownerToken, 1, null, null);
        }

        var existingParams = new { TenantId = tenantId.ToString(), Scope = scope, Key = key.Value };
        var existing = await connection.QuerySingleOrDefaultAsync<MySqlRecordDto>(
            new CommandDefinition(SelectExistingSql, existingParams, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (existing is null)
        {
            return new IdempotencyClaimResult(ClaimResultStatus.InFlightConflict, null, null, null, null);
        }

        if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return new IdempotencyClaimResult(ClaimResultStatus.FingerprintMismatch, null, null, null, existing.Fingerprint);
        }

        if (existing.Status == (byte)IdempotencyStatus.Completed)
        {
            var headers = string.IsNullOrWhiteSpace(existing.ResponseHeaders)
                ? new Dictionary<string, string[]>()
                : JsonSerializer.Deserialize(existing.ResponseHeaders, IdempotencyJsonContext.Default.DictionaryStringStringArray)
                  ?? new Dictionary<string, string[]>();

            var cachedResponse = new CachedIdempotencyResponse(
                existing.ResponseStatusCode ?? 200,
                headers,
                existing.ResponseBody ?? ReadOnlyMemory<byte>.Empty);

            return new IdempotencyClaimResult(ClaimResultStatus.CompletedReplay, null, null, cachedResponse, existing.Fingerprint);
        }

        if (existing.Status == (byte)IdempotencyStatus.Processing && existing.LeaseExpiresAtUtc >= now.UtcDateTime)
        {
            return new IdempotencyClaimResult(ClaimResultStatus.InFlightConflict, null, null, null, existing.Fingerprint);
        }

        var stealParams = new
        {
            TenantId = tenantId.ToString(),
            Scope = scope,
            Key = key.Value,
            NewOwnerToken = ownerToken.ToString(),
            Fingerprint = fingerprint,
            NewLeaseExpiresAt = leaseExpiresAt.UtcDateTime,
            Now = now.UtcDateTime
        };

        var updatedRows = await connection.ExecuteAsync(
            new CommandDefinition(StealLeaseSql, stealParams, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (updatedRows > 0)
        {
            return new IdempotencyClaimResult(ClaimResultStatus.AcquiredStale, ownerToken, existing.ConcurrencyVersion + 1, null, null);
        }

        return new IdempotencyClaimResult(ClaimResultStatus.InFlightConflict, null, null, null, existing.Fingerprint);
    }

    /// <inheritdoc />
    public async Task<bool> MarkCompletedAsync(
        Guid tenantId,
        string scope,
        IdempotencyKey key,
        Guid ownerToken,
        int concurrencyVersion,
        int statusCode,
        IReadOnlyDictionary<string, string[]> headers,
        ReadOnlyMemory<byte> responseBody,
        TimeSpan retentionDuration,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await MarkCompletedCoreAsync(
            connection, null, tenantId, scope, key, ownerToken, concurrencyVersion, statusCode, headers, responseBody, retentionDuration, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<bool> MarkCompletedCoreAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        Guid tenantId,
        string scope,
        IdempotencyKey key,
        Guid ownerToken,
        int concurrencyVersion,
        int statusCode,
        IReadOnlyDictionary<string, string[]> headers,
        ReadOnlyMemory<byte> responseBody,
        TimeSpan retentionDuration,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var headersJson = JsonSerializer.Serialize(headers, IdempotencyJsonContext.Default.IReadOnlyDictionaryStringStringArray);

        var parameters = new
        {
            TenantId = tenantId.ToString(),
            Scope = scope,
            Key = key.Value,
            OwnerToken = ownerToken.ToString(),
            ConcurrencyVersion = concurrencyVersion,
            StatusCode = statusCode,
            Headers = headersJson,
            ResponseBody = responseBody.ToArray(),
            Now = now.UtcDateTime,
            RetentionExpiresAt = now.Add(retentionDuration).UtcDateTime
        };

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(MarkCompletedSql, parameters, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<bool> MarkFailedAsync(
        Guid tenantId,
        string scope,
        IdempotencyKey key,
        Guid ownerToken,
        int concurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await MarkFailedCoreAsync(
            connection, null, tenantId, scope, key, ownerToken, concurrencyVersion, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<bool> MarkFailedCoreAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        Guid tenantId,
        string scope,
        IdempotencyKey key,
        Guid ownerToken,
        int concurrencyVersion,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var parameters = new
        {
            TenantId = tenantId.ToString(),
            Scope = scope,
            Key = key.Value,
            OwnerToken = ownerToken.ToString(),
            ConcurrencyVersion = concurrencyVersion,
            Now = now.UtcDateTime
        };

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(MarkFailedSql, parameters, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<int> CleanupExpiredRecordsAsync(
        DateTimeOffset utcNow,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await CleanupExpiredRecordsCoreAsync(connection, null, utcNow, batchSize, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<int> CleanupExpiredRecordsCoreAsync(
        IDbConnection connection,
        IDbTransaction? transaction,
        DateTimeOffset utcNow,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var parameters = new { UtcNow = utcNow.UtcDateTime, BatchSize = batchSize };
        return await connection.ExecuteAsync(
            new CommandDefinition(CleanupBatchSql, parameters, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    internal sealed class MySqlRecordDto
    {
        public string Id { get; init; } = null!;
        public string TenantId { get; init; } = null!;
        public string Scope { get; set; } = null!;
        public string IdempotencyKey { get; set; } = null!;
        public string Fingerprint { get; set; } = null!;
        public byte Status { get; init; }
        public string OwnerToken { get; init; } = null!;
        public int ConcurrencyVersion { get; init; }
        public int? ResponseStatusCode { get; init; }
        public string? ResponseHeaders { get; init; }
        public byte[]? ResponseBody { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime LeaseExpiresAtUtc { get; init; }
        public DateTime? CompletedAtUtc { get; init; }
        public DateTime RetentionExpiresAtUtc { get; init; }
    }
}
