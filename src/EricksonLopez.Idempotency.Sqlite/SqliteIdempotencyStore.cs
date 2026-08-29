// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;

namespace EricksonLopez.Idempotency.Sqlite;

/// <summary>
/// Provides an SQLite persistence store implementation for <see cref="IIdempotencyStore"/> using Dapper and Microsoft.Data.Sqlite.
/// </summary>
public sealed class SqliteIdempotencyStore : IIdempotencyStore
{
    private readonly string _connectionString;

    private const string InsertCommandSql = """
        INSERT INTO idempotency_records (
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
        WHERE rowid IN (
            SELECT rowid FROM idempotency_records
            WHERE retention_expires_at_utc < @UtcNow
            LIMIT @BatchSize
        );
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteIdempotencyStore"/> class with the specified connection string.
    /// </summary>
    /// <param name="connectionString">The SQLite database connection string.</param>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public SqliteIdempotencyStore(string connectionString)
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
        var now = DateTimeOffset.UtcNow;
        var leaseExpiresAt = now.Add(leaseDuration);
        var retentionExpiresAt = now.Add(retentionDuration);
        var ownerToken = Guid.NewGuid();
        var recordId = Guid.NewGuid();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var insertParams = new
        {
            Id = recordId.ToString(),
            TenantId = tenantId.ToString(),
            Scope = scope,
            Key = key.Value,
            Fingerprint = fingerprint,
            OwnerToken = ownerToken.ToString(),
            Now = now.ToString("O", CultureInfo.InvariantCulture),
            LeaseExpiresAt = leaseExpiresAt.ToString("O", CultureInfo.InvariantCulture),
            RetentionExpiresAt = retentionExpiresAt.ToString("O", CultureInfo.InvariantCulture)
        };

        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(InsertCommandSql, insertParams, cancellationToken: cancellationToken)).ConfigureAwait(false);

            return new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, ownerToken, 1, null, null);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLite Error 19: 'PRIMARY KEY must be unique'.
        {
            // Fall through to query existing
        }

        var existingParams = new { TenantId = tenantId.ToString(), Scope = scope, Key = key.Value };
        var existing = await connection.QuerySingleOrDefaultAsync<SqliteRecordDto>(
            new CommandDefinition(SelectExistingSql, existingParams, cancellationToken: cancellationToken)).ConfigureAwait(false);

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

        var existingLease = DateTimeOffset.Parse(existing.LeaseExpiresAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (existing.Status == (byte)IdempotencyStatus.Processing && existingLease >= now)
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
            NewLeaseExpiresAt = leaseExpiresAt.ToString("O", CultureInfo.InvariantCulture),
            Now = now.ToString("O", CultureInfo.InvariantCulture)
        };

        var updatedRows = await connection.ExecuteAsync(
            new CommandDefinition(StealLeaseSql, stealParams, cancellationToken: cancellationToken)).ConfigureAwait(false);

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
        var now = DateTimeOffset.UtcNow;
        var headersJson = JsonSerializer.Serialize(headers, IdempotencyJsonContext.Default.IReadOnlyDictionaryStringStringArray);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

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
            Now = now.ToString("O", CultureInfo.InvariantCulture),
            RetentionExpiresAt = now.Add(retentionDuration).ToString("O", CultureInfo.InvariantCulture)
        };

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(MarkCompletedSql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);

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
        var now = DateTimeOffset.UtcNow;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var parameters = new
        {
            TenantId = tenantId.ToString(),
            Scope = scope,
            Key = key.Value,
            OwnerToken = ownerToken.ToString(),
            ConcurrencyVersion = concurrencyVersion,
            Now = now.ToString("O", CultureInfo.InvariantCulture)
        };

        var affected = await connection.ExecuteAsync(
            new CommandDefinition(MarkFailedSql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<int> CleanupExpiredRecordsAsync(
        DateTimeOffset utcNow,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var parameters = new
        {
            UtcNow = utcNow.ToString("O", CultureInfo.InvariantCulture),
            BatchSize = batchSize
        };

        return await connection.ExecuteAsync(
            new CommandDefinition(CleanupBatchSql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private sealed class SqliteRecordDto
    {
        public string Id { get; set; } = null!;
        public string TenantId { get; set; } = null!;
        public string Scope { get; set; } = null!;
        public string IdempotencyKey { get; set; } = null!;
        public string Fingerprint { get; set; } = null!;
        public byte Status { get; set; }
        public string OwnerToken { get; set; } = null!;
        public int ConcurrencyVersion { get; set; }
        public int? ResponseStatusCode { get; set; }
        public string? ResponseHeaders { get; set; }
        public byte[]? ResponseBody { get; set; }
        public string CreatedAtUtc { get; set; } = null!;
        public string LeaseExpiresAtUtc { get; set; } = null!;
        public string? CompletedAtUtc { get; set; }
        public string RetentionExpiresAtUtc { get; set; } = null!;
    }
}
