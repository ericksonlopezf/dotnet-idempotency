// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Idempotency.Testing;

/// <summary>
/// Provides a thread-safe in-memory implementation of <see cref="IIdempotencyStore"/> for unit testing and local development.
/// </summary>
/// <remarks>
/// This store maintains records in a thread-safe in-memory collection and supports deterministic time
/// control via <see cref="TimeProvider"/>. It is intended for testing and development environments only.
/// </remarks>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, StoredRecord> _records = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryIdempotencyStore"/> class.
    /// </summary>
    /// <param name="timeProvider">Optional time provider for deterministic time manipulation in unit tests.</param>
    public InMemoryIdempotencyStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task<IdempotencyClaimResult> TryAcquireAsync(
        Guid tenantId,
        string scope,
        IdempotencyKey key,
        string fingerprint,
        TimeSpan leaseDuration,
        TimeSpan retentionDuration,
        CancellationToken cancellationToken = default)
    {
        var recordKey = BuildKey(tenantId, scope, key);
        var now = _timeProvider.GetUtcNow();
        var ownerToken = Guid.NewGuid();
        var leaseExpiresAt = now.Add(leaseDuration);
        var retentionExpiresAt = now.Add(retentionDuration);

        while (true)
        {
            if (_records.TryGetValue(recordKey, out var existing))
            {
                // 1. Check fingerprint mismatch
                if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    return Task.FromResult(new IdempotencyClaimResult(ClaimResultStatus.FingerprintMismatch, null, null, null, existing.Fingerprint));
                }

                // 2. Check if completed
                if (existing.Status == IdempotencyStatus.Completed)
                {
                    var cachedResponse = new CachedIdempotencyResponse(
                        existing.StatusCode,
                        existing.Headers ?? new Dictionary<string, string[]>(),
                        existing.ResponseBody);
                    return Task.FromResult(new IdempotencyClaimResult(ClaimResultStatus.CompletedReplay, null, null, cachedResponse, existing.Fingerprint));
                }

                // 3. Check if active processing
                if (existing.Status == IdempotencyStatus.Processing && existing.LeaseExpiresAtUtc >= now)
                {
                    return Task.FromResult(new IdempotencyClaimResult(ClaimResultStatus.InFlightConflict, null, null, null, existing.Fingerprint));
                }

                // 4. Steal expired lease or retry failed record
                var updated = existing with
                {
                    Status = IdempotencyStatus.Processing,
                    OwnerToken = ownerToken,
                    ConcurrencyVersion = existing.ConcurrencyVersion + 1,
                    LeaseExpiresAtUtc = leaseExpiresAt
                };

                if (_records.TryUpdate(recordKey, updated, existing))
                {
                    return Task.FromResult(new IdempotencyClaimResult(ClaimResultStatus.AcquiredStale, ownerToken, updated.ConcurrencyVersion, null, null));
                }
            }
            else
            {
                // Record does not exist: attempt insertion
                var newRecord = new StoredRecord(
                    Fingerprint: fingerprint,
                    Status: IdempotencyStatus.Processing,
                    OwnerToken: ownerToken,
                    ConcurrencyVersion: 1,
                    StatusCode: 0,
                    Headers: null,
                    ResponseBody: ReadOnlyMemory<byte>.Empty,
                    LeaseExpiresAtUtc: leaseExpiresAt,
                    RetentionExpiresAtUtc: retentionExpiresAt);

                if (_records.TryAdd(recordKey, newRecord))
                {
                    return Task.FromResult(new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, ownerToken, 1, null, null));
                }
            }
        }
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
        CancellationToken cancellationToken = default)
    {
        var recordKey = BuildKey(tenantId, scope, key);
        var now = _timeProvider.GetUtcNow();

        if (_records.TryGetValue(recordKey, out var existing))
        {
            if (existing.OwnerToken == ownerToken && existing.ConcurrencyVersion == concurrencyVersion)
            {
                var updated = existing with
                {
                    Status = IdempotencyStatus.Completed,
                    StatusCode = statusCode,
                    Headers = headers,
                    ResponseBody = responseBody,
                    RetentionExpiresAtUtc = now.Add(retentionDuration)
                };

                return Task.FromResult(_records.TryUpdate(recordKey, updated, existing));
            }
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<bool> MarkFailedAsync(
        Guid tenantId,
        string scope,
        IdempotencyKey key,
        Guid ownerToken,
        int concurrencyVersion,
        CancellationToken cancellationToken = default)
    {
        var recordKey = BuildKey(tenantId, scope, key);

        if (_records.TryGetValue(recordKey, out var existing))
        {
            if (existing.OwnerToken == ownerToken && existing.ConcurrencyVersion == concurrencyVersion)
            {
                var updated = existing with
                {
                    Status = IdempotencyStatus.Failed
                };

                return Task.FromResult(_records.TryUpdate(recordKey, updated, existing));
            }
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<int> CleanupExpiredRecordsAsync(
        DateTimeOffset utcNow,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var expiredKeys = _records
            .Where(r => r.Value.RetentionExpiresAtUtc < utcNow)
            .Take(batchSize)
            .Select(r => r.Key)
            .ToList();

        var count = 0;
        foreach (var k in expiredKeys)
        {
            if (_records.TryRemove(k, out _))
            {
                count++;
            }
        }

        return Task.FromResult(count);
    }

    /// <summary>
    /// Removes all stored idempotency records from memory.
    /// </summary>
    public void Clear() => _records.Clear();

    private static string BuildKey(Guid tenantId, string scope, IdempotencyKey key) => $"{tenantId:D}:{scope}:{key.Value}";

    private sealed record StoredRecord(
        string Fingerprint,
        IdempotencyStatus Status,
        Guid OwnerToken,
        int ConcurrencyVersion,
        int StatusCode,
        IReadOnlyDictionary<string, string[]>? Headers,
        ReadOnlyMemory<byte> ResponseBody,
        DateTimeOffset LeaseExpiresAtUtc,
        DateTimeOffset RetentionExpiresAtUtc);
}
