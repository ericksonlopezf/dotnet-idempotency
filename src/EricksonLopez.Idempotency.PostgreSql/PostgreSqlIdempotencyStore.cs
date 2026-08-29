// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;

namespace EricksonLopez.Idempotency.PostgreSql;

/// <summary>
/// Provides a PostgreSQL persistence store implementation for <see cref="IIdempotencyStore"/> and
/// <see cref="ITransactionalIdempotencyStore"/> using Dapper and <see cref="NpgsqlDataSource"/>.
/// </summary>
public sealed class PostgreSqlIdempotencyStore : ITransactionalIdempotencyStore
{
    private readonly NpgsqlDataSource _dataSource;

    private const string InsertCommandSql = """
        INSERT INTO idempotency_records (
            id, tenant_id, scope, idempotency_key, fingerprint, status,
            owner_token, concurrency_version, created_at_utc, lease_expires_at_utc, retention_expires_at_utc
        )
        VALUES (
            @Id, @TenantId, @Scope, @Key, @Fingerprint, 1,
            @OwnerToken, 1, @Now, @LeaseExpiresAt, @RetentionExpiresAt
        )
        ON CONFLICT (tenant_id, scope, idempotency_key) DO NOTHING;
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
          )
        RETURNING concurrency_version;
        """;

    private const string MarkCompletedSql = """
        UPDATE idempotency_records
        SET status = 2,
            response_status_code = @StatusCode,
            response_headers = @Headers::jsonb,
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
        WHERE ctid IN (
            SELECT ctid FROM idempotency_records
            WHERE retention_expires_at_utc < @UtcNow
            LIMIT @BatchSize
        );
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSqlIdempotencyStore"/> class with the specified PostgreSQL data source.
    /// </summary>
    /// <param name="dataSource">The configured <see cref="NpgsqlDataSource"/> data source.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dataSource"/> is <see langword="null"/></exception>
    public PostgreSqlIdempotencyStore(NpgsqlDataSource dataSource)
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
            Id = recordId,
            TenantId = tenantId,
            Scope = scope,
            Key = key.Value,
            Fingerprint = fingerprint,
            OwnerToken = ownerToken,
            Now = now,
            LeaseExpiresAt = leaseExpiresAt,
            RetentionExpiresAt = retentionExpiresAt
        };

        var rowsInserted = await connection.ExecuteAsync(
            new CommandDefinition(InsertCommandSql, insertParams, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (rowsInserted > 0)
        {
            return new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, ownerToken, 1, null, null);
        }

        // Row already exists - inspect current state
        var existingParams = new { TenantId = tenantId, Scope = scope, Key = key.Value };
        var existing = await connection.QuerySingleOrDefaultAsync<PostgresRecordDto>(
            new CommandDefinition(SelectExistingSql, existingParams, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (existing is null)
        {
            return new IdempotencyClaimResult(ClaimResultStatus.InFlightConflict, null, null, null, null);
        }

        // 1. Check fingerprint mismatch
        if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return new IdempotencyClaimResult(ClaimResultStatus.FingerprintMismatch, null, null, null, existing.Fingerprint);
        }

        // 2. Check if already completed
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

        // 3. Check if currently processing with active lease
        if (existing.Status == (byte)IdempotencyStatus.Processing && existing.LeaseExpiresAtUtc >= now)
        {
            return new IdempotencyClaimResult(ClaimResultStatus.InFlightConflict, null, null, null, existing.Fingerprint);
        }

        // 4. Try steal expired lease or retry failed record
        var stealParams = new
        {
            TenantId = tenantId,
            Scope = scope,
            Key = key.Value,
            NewOwnerToken = ownerToken,
            Fingerprint = fingerprint,
            NewLeaseExpiresAt = leaseExpiresAt,
            Now = now
        };

        var updatedVersion = await connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(StealLeaseSql, stealParams, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (updatedVersion.HasValue)
        {
            return new IdempotencyClaimResult(ClaimResultStatus.AcquiredStale, ownerToken, updatedVersion.Value, null, null);
        }

        return new IdempotencyClaimResult(ClaimResultStatus.InFlightConflict, null, null, null, existing.Fingerprint);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses a new database connection obtained from the configured <see cref="NpgsqlDataSource"/>.
    /// To participate in an existing transaction, use the <see cref="ITransactionalIdempotencyStore"/> overload.
    /// </remarks>
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
            connection, null, tenantId, scope, key, ownerToken, concurrencyVersion,
            statusCode, headers, responseBody, retentionDuration, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Participates in the provided <paramref name="transaction"/>. The caller is responsible for
    /// committing or rolling back the transaction.
    /// </remarks>
    public Task<bool> MarkCompletedAsync(
        Guid tenantId,
        string scope,
        IdempotencyKey key,
        Guid ownerToken,
        int concurrencyVersion,
        int statusCode,
        IReadOnlyDictionary<string, string[]> headers,
        ReadOnlyMemory<byte> responseBody,
        TimeSpan retentionDuration,
        IDbConnection connection,
        IDbTransaction? transaction,
        CancellationToken cancellationToken = default) =>
        MarkCompletedCoreAsync(
            connection, transaction, tenantId, scope, key, ownerToken, concurrencyVersion,
            statusCode, headers, responseBody, retentionDuration, cancellationToken);

    private static async Task<bool> MarkCompletedCoreAsync(
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
            TenantId = tenantId,
            Scope = scope,
            Key = key.Value,
            OwnerToken = ownerToken,
            ConcurrencyVersion = concurrencyVersion,
            StatusCode = statusCode,
            Headers = headersJson,
            ResponseBody = responseBody.ToArray(),
            Now = now,
            RetentionExpiresAt = now.Add(retentionDuration)
        };

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(MarkCompletedSql, parameters, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return affected > 0;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses a new database connection obtained from the configured <see cref="NpgsqlDataSource"/>.
    /// To participate in an existing transaction, use the <see cref="ITransactionalIdempotencyStore"/> overload.
    /// </remarks>
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

    /// <inheritdoc />
    /// <remarks>
    /// Participates in the provided <paramref name="transaction"/>. The caller is responsible for
    /// committing or rolling back the transaction.
    /// </remarks>
    public Task<bool> MarkFailedAsync(
        Guid tenantId,
        string scope,
        IdempotencyKey key,
        Guid ownerToken,
        int concurrencyVersion,
        IDbConnection connection,
        IDbTransaction? transaction,
        CancellationToken cancellationToken = default) =>
        MarkFailedCoreAsync(connection, transaction, tenantId, scope, key, ownerToken, concurrencyVersion, cancellationToken);

    private static async Task<bool> MarkFailedCoreAsync(
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
            TenantId = tenantId,
            Scope = scope,
            Key = key.Value,
            OwnerToken = ownerToken,
            ConcurrencyVersion = concurrencyVersion,
            Now = now
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
        var parameters = new { UtcNow = utcNow, BatchSize = batchSize };
        return await connection.ExecuteAsync(
            new CommandDefinition(CleanupBatchSql, parameters, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}
