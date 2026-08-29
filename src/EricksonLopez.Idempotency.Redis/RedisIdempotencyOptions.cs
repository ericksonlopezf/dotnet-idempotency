// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Idempotency.Redis;

/// <summary>
/// Encapsulates configuration options for the Redis idempotency store.
/// </summary>
public sealed class RedisIdempotencyOptions
{
    /// <summary>
    /// Gets or sets the key prefix applied to all Redis keys managed by this store.
    /// </summary>
    /// <remarks>
    /// All idempotency record keys are namespaced under this prefix to avoid collisions
    /// with other Redis data. Defaults to <c>"idempotency:"</c>.
    /// </remarks>
    public string KeyPrefix { get; set; } = "idempotency:";
}
