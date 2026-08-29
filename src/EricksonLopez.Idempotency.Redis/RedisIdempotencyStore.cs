// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace EricksonLopez.Idempotency.Redis;

/// <summary>
/// Provides a Redis-backed implementation of <see cref="IIdempotencyStore"/> using
/// <see cref="IConnectionMultiplexer"/> with Lua scripts for atomic operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>Correctness guarantee note</b>: This provider offers <em>strong</em> exactly-once guarantees
/// for the common case but cannot provide the same fencing token guarantees as SQL providers.
/// Specifically:
/// <list type="bullet">
///   <item><description>Concurrent acquisition is prevented by a Lua atomic script.</description></item>
///   <item><description>In-flight conflict detection works correctly for typical latency ranges.</description></item>
///   <item><description>Zombie worker protection via <c>concurrency_version</c> is best-effort;
///   a zombie worker that completes after TTL expiry may not be detected.</description></item>
/// </list>
/// For critical financial operations, prefer the SQL providers (PostgreSQL, SQL Server) which implement
/// true fencing tokens via conditional SQL UPDATE statements.
/// </para>
/// <para>
/// See <c>docs/adr/adr-013-no-idistributedcache-abstraction.md</c> for the design rationale.
/// </para>
/// </remarks>
public sealed class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisIdempotencyOptions _options;

    // Lua script: atomic acquire or detect existing record
    // KEYS[1] = composite key
    // ARGV[1] = fingerprint, ARGV[2] = ownerToken, ARGV[3] = leaseTtlMs
    // Returns array: [resultCode, status_code_or_nil, data_or_nil, fingerprint_or_nil]
    // resultCode: 1=acquired, 2=completed(replay), 3=in-flight, 4=fingerprint mismatch
    private const string AcquireScript = @"
local key = KEYS[1]
local existing = redis.call('HGETALL', key)
if #existing == 0 then
    redis.call('HSET', key, 'status', 'InFlight', 'fp', ARGV[1], 'owner', ARGV[2], 'ver', 1)
    redis.call('PEXPIRE', key, ARGV[3])
    return {1, false, false, ARGV[1]}
end
local map = {}
for i = 1, #existing, 2 do map[existing[i]] = existing[i+1] end
local st = map['status']
local fp = map['fp']
if fp ~= ARGV[1] then return {4, false, false, fp} end
if st == 'Completed' then return {2, map['sc'], map['data'], fp} end
if st == 'InFlight' then return {3, false, false, fp} end
redis.call('HSET', key, 'status', 'InFlight', 'owner', ARGV[2], 'ver', tonumber(map['ver'] or 0)+1)
redis.call('PEXPIRE', key, ARGV[3])
return {1, false, false, ARGV[1]}
";

    // Lua script: conditional complete (owner-token check)
    // KEYS[1] = composite key
    // ARGV[1] = ownerToken, ARGV[2] = statusCode, ARGV[3] = serialized data, ARGV[4] = retentionTtlMs
    private const string CompleteScript = @"
local key = KEYS[1]
if redis.call('HGET', key, 'owner') ~= ARGV[1] then return 0 end
redis.call('HSET', key, 'status', 'Completed', 'sc', ARGV[2], 'data', ARGV[3])
redis.call('PEXPIRE', key, ARGV[4])
return 1
";

    // Lua script: conditional fail (owner-token check)
    // KEYS[1] = composite key
    // ARGV[1] = ownerToken
    private const string FailScript = @"
local key = KEYS[1]
if redis.call('HGET', key, 'owner') ~= ARGV[1] then return 0 end
redis.call('HSET', key, 'status', 'Failed')
redis.call('PEXPIRE', key, 300000)
return 1
";

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisIdempotencyStore"/> class with the specified Redis connection and options.
    /// </summary>
    /// <param name="redis">The Redis connection multiplexer.</param>
    /// <param name="options">The Redis store configuration options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="redis"/> or <paramref name="options"/> is <see langword="null"/></exception>
    public RedisIdempotencyStore(IConnectionMultiplexer redis, RedisIdempotencyOptions options)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _options = options ?? throw new ArgumentNullException(nameof(options));
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
        var redisKey = BuildKey(tenantId, scope, key.Value);
        var ownerToken = Guid.NewGuid();
        var leaseTtlMs = (long)leaseDuration.TotalMilliseconds;

        var db = _redis.GetDatabase();
        var result = (RedisValue[])(await db.ScriptEvaluateAsync(
            AcquireScript,
            keys: new RedisKey[] { redisKey },
            values: new RedisValue[] { fingerprint, ownerToken.ToString(), leaseTtlMs }).ConfigureAwait(false))!;

        var code = (int)result[0];

        return code switch
        {
            1 => new IdempotencyClaimResult(ClaimResultStatus.AcquiredNew, ownerToken, 1, null, null),
            2 => new IdempotencyClaimResult(ClaimResultStatus.CompletedReplay, null, null, DeserializeCachedResponse((string?)result[2], (int?)result[1]), null),
            3 => new IdempotencyClaimResult(ClaimResultStatus.InFlightConflict, null, null, null, null),
            4 => new IdempotencyClaimResult(ClaimResultStatus.FingerprintMismatch, null, null, null, (string?)result[3]),
            _ => new IdempotencyClaimResult(ClaimResultStatus.InFlightConflict, null, null, null, null)
        };
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
        var redisKey = BuildKey(tenantId, scope, key.Value);
        var payload = JsonSerializer.Serialize(
            new RedisResponsePayload(statusCode, headers, responseBody.ToArray()),
            RedisIdempotencyJsonContext.Default.RedisResponsePayload);

        var ttlMs = (long)retentionDuration.TotalMilliseconds;
        var db = _redis.GetDatabase();

        var result = (int)(await db.ScriptEvaluateAsync(
            CompleteScript,
            keys: new RedisKey[] { redisKey },
            values: new RedisValue[] { ownerToken.ToString(), statusCode, payload, ttlMs }).ConfigureAwait(false));

        return result == 1;
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
        var redisKey = BuildKey(tenantId, scope, key.Value);
        var db = _redis.GetDatabase();

        var result = (int)(await db.ScriptEvaluateAsync(
            FailScript,
            keys: new RedisKey[] { redisKey },
            values: new RedisValue[] { ownerToken.ToString() }).ConfigureAwait(false));

        return result == 1;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Redis stores use TTL-based eviction rather than explicit cleanup. This method
    /// returns 0 and performs no operation because Redis keys expire automatically.
    /// </remarks>
    public Task<int> CleanupExpiredRecordsAsync(
        DateTimeOffset utcNow,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        // Redis handles expiration via PEXPIRE/EXPIRE set on each key.
        // No explicit cleanup is needed.
        return Task.FromResult(0);
    }

    private string BuildKey(Guid tenantId, string scope, string keyValue) =>
        $"{_options.KeyPrefix}{tenantId}:{scope}:{keyValue}";

    private static CachedIdempotencyResponse? DeserializeCachedResponse(string? json, int? statusCode)
    {
        if (json is null || statusCode is null)
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize(json, RedisIdempotencyJsonContext.Default.RedisResponsePayload);
            if (payload is not null)
            {
                return new CachedIdempotencyResponse(
                    payload.StatusCode,
                    payload.Headers,
                    payload.Body);
            }
        }
        catch (JsonException)
        {
            // Corrupted cache payload falls back to null
        }

        return null;
    }
}

