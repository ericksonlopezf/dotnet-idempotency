// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Oracle.ManagedDataAccess.Client;

namespace EricksonLopez.Idempotency.Oracle;

/// <summary>
/// Provides an Oracle Database persistence store implementation for <see cref="IIdempotencyStore"/> using Dapper and raw SQL.
/// </summary>
public sealed class OracleIdempotencyStore : ITransactionalIdempotencyStore
{
    private readonly string _connectionString;

    private const string InsertCommandSql = """
        INSERT INTO idempotency_records (
            id, tenant_id, scope, idempotency_key, fingerprint, status,
            owner_token, concurrency_version, created_at_utc, lease_expires_at_utc, retention_expires_at_utc
        )
        VALUES (
            :Id, :TenantId, :Scope, :Key, :Fingerprint, 1,
            :OwnerToken, 1, :Now, :LeaseExpiresAt, :RetentionExpiresAt
        )
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
        WHERE tenant_id = :TenantId AND scope = :Scope AND idempotency_key = :Key
        """;

    private const string StealLeaseSql = """
        UPDATE idempotency_records
        SET owner_token = :NewOwnerToken,
            concurrency_version = concurrency_version + 1,
            status = 1,
            fingerprint = :Fingerprint,
            lease_expires_at_utc = :NewLeaseExpiresAt,
            created_at_utc = :Now
        WHERE tenant_id = :TenantId
          AND scope = :Scope
          AND idempotency_key = :Key
          AND (
              (status = 1 AND lease_expires_at_utc < :Now)
              OR status = 3
          )
        """;

    private const string MarkCompletedSql = """
        UPDATE idempotency_records
        SET status = 2,
            response_status_code = :StatusCode,
            response_headers = :Headers,
            response_body = :ResponseBody,
            completed_at_utc = :Now,
            retention_expires_at_utc = :RetentionExpiresAt
        WHERE tenant_id = :TenantId
          AND scope = :Scope
          AND idempotency_key = :Key
          AND owner_token = :OwnerToken
          AND concurrency_version = :ConcurrencyVersion
        """;

    private const string MarkFailedSql = """
        UPDATE idempotency_records
        SET status = 3,
            completed_at_utc = :Now
        WHERE tenant_id = :TenantId
          AND scope = :Scope
          AND idempotency_key = :Key
          AND owner_token = :OwnerToken
          AND concurrency_version = :ConcurrencyVersion
        """;

    private const string CleanupBatchSql = """
        DELETE FROM idempotency_records
        WHERE retention_expires_at_utc < :UtcNow
          AND ROWNUM <= :BatchSize
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="OracleIdempotencyStore"/> class with the specified connection string.
    /// </summary>
    /// <param name="connectionString">The Oracle database connection string.</param>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public OracleIdempotencyStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
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
        await using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
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
            Id = recordId.ToByteArray(),
            TenantId = tenantId.ToByteArray(),
            Scope = scope,
            Key = key.Value,
            Fingerprint = fingerprint,
            OwnerToken = ownerToken.ToByteArray(),
            Now = now,
            LeaseExpiresAt = leaseExpiresAt,
            RetentionExpiresAt = retentionExpiresAt
        };

        try
        {
            var rowsInserted = await connection.ExecuteAsync(
                new CommandDefinition(InsertCommandSql, insertParams, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (rowsInserted > 0)
            {
                return new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, ownerToken, 1, null, null);
            }
        }
        catch (DbException)
        {
            // Primary key / unique constraint violation - fall through to select existing
        }

        var existingParams = new { TenantId = tenantId.ToByteArray(), Scope = scope, Key = key.Value };
        var existing = await connection.QuerySingleOrDefaultAsync<OracleRecordDto>(
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

        if (existing.Status == (byte)IdempotencyStatus.Processing && existing.LeaseExpiresAtUtc >= now)
        {
            return new IdempotencyClaimResult(ClaimResultStatus.InFlightConflict, null, null, null, existing.Fingerprint);
        }

        var stealParams = new
        {
            TenantId = tenantId.ToByteArray(),
            Scope = scope,
            Key = key.Value,
            NewOwnerToken = ownerToken.ToByteArray(),
            Fingerprint = fingerprint,
            NewLeaseExpiresAt = leaseExpiresAt,
            Now = now
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
        await using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await MarkCompletedCoreAsync(
            connection, null, tenantId, scope, key, ownerToken, concurrencyVersion, statusCode, headers, responseBody, retentionDuration, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
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
            connection, transaction, tenantId, scope, key, ownerToken, concurrencyVersion, statusCode, headers, responseBody, retentionDuration, cancellationToken);

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
            TenantId = tenantId.ToByteArray(),
            Scope = scope,
            Key = key.Value,
            OwnerToken = ownerToken.ToByteArray(),
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
    public async Task<bool> MarkFailedAsync(
        Guid tenantId,
        string scope,
        IdempotencyKey key,
        Guid ownerToken,
        int concurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await MarkFailedCoreAsync(
            connection, null, tenantId, scope, key, ownerToken, concurrencyVersion, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
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
            TenantId = tenantId.ToByteArray(),
            Scope = scope,
            Key = key.Value,
            OwnerToken = ownerToken.ToByteArray(),
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
        await using var connection = new OracleConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
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

    internal sealed class OracleRecordDto
    {
        public byte[]? Id { get; init; }
        public byte[]? TenantId { get; init; }
        public string Scope { get; set; } = null!;
        public string IdempotencyKey { get; set; } = null!;
        public string Fingerprint { get; set; } = null!;
        public byte Status { get; init; }
        public byte[]? OwnerToken { get; init; }
        public int ConcurrencyVersion { get; init; }
        public int? ResponseStatusCode { get; init; }
        public string? ResponseHeaders { get; init; }
        public byte[]? ResponseBody { get; init; }
        public DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset LeaseExpiresAtUtc { get; init; }
        public DateTimeOffset? CompletedAtUtc { get; init; }
        public DateTimeOffset RetentionExpiresAtUtc { get; init; }
    }
}
