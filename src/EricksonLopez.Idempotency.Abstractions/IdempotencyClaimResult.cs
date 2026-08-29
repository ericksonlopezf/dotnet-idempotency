// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Idempotency;

/// <summary>
/// Represents the result of an atomic key acquisition attempt against the idempotency store.
/// </summary>
/// <param name="Status">The specific status outcome of the claim attempt.</param>
/// <param name="OwnerToken">The unique owner token issued if acquisition succeeded.</param>
/// <param name="ConcurrencyVersion">The concurrency version token if acquisition succeeded.</param>
/// <param name="CachedResponse">The cached response payload if status is <see cref="ClaimResultStatus.CompletedReplay"/>.</param>
/// <param name="ExistingFingerprint">The recorded fingerprint on collision or mismatch.</param>
public sealed record IdempotencyClaimResult(
    ClaimResultStatus Status,
    Guid? OwnerToken,
    int? ConcurrencyVersion,
    CachedIdempotencyResponse? CachedResponse,
    string? ExistingFingerprint)
{
    /// <summary>
    /// Gets a value indicating whether ownership of the idempotency key was successfully acquired to execute the operation.
    /// </summary>
    public bool IsAcquired => Status is ClaimResultStatus.AcquiredNew or ClaimResultStatus.AcquiredStale;

    /// <summary>
    /// Gets a value indicating whether this claim represents a replay of a previously completed operation.
    /// </summary>
    public bool IsReplay => Status is ClaimResultStatus.CompletedReplay;
}
