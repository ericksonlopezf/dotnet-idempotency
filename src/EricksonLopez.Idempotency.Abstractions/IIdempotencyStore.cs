// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Idempotency;

/// <summary>
/// Defines the persistence contract for atomically recording, updating, and querying idempotency state across distributed instances.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Attempts to acquire exclusive execution ownership for an idempotency key within the specified tenant and scope.
    /// </summary>
    /// <param name="tenantId">The unique identifier of the tenant.</param>
    /// <param name="scope">The functional scope or partition of the operation.</param>
    /// <param name="key">The unique idempotency key.</param>
    /// <param name="fingerprint">The cryptographic fingerprint representing the canonical request.</param>
    /// <param name="leaseDuration">The duration for which the ownership lease remains valid before considering it stale.</param>
    /// <param name="retentionDuration">The retention period for retaining completed records.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains the <see cref="IdempotencyClaimResult"/>
    /// describing the acquisition outcome.
    /// </returns>
    Task<IdempotencyClaimResult> TryAcquireAsync(
        Guid tenantId,
        string scope,
        IdempotencyKey key,
        string fingerprint,
        TimeSpan leaseDuration,
        TimeSpan retentionDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically marks an in-flight idempotency record as completed, storing the produced response payload.
    /// </summary>
    /// <param name="tenantId">The unique identifier of the tenant.</param>
    /// <param name="scope">The functional scope of the operation.</param>
    /// <param name="key">The idempotency key.</param>
    /// <param name="ownerToken">The ownership token issued during key acquisition.</param>
    /// <param name="concurrencyVersion">The concurrency version issued during key acquisition.</param>
    /// <param name="statusCode">The HTTP or logical status code to cache.</param>
    /// <param name="headers">The response headers to cache.</param>
    /// <param name="responseBody">The serialized response body bytes to cache.</param>
    /// <param name="retentionDuration">The retention duration for the completed record.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains <see langword="true"/> if the record was successfully
    /// updated with matching fencing tokens; otherwise, <see langword="false"/>.
    /// </returns>
    Task<bool> MarkCompletedAsync(
        Guid tenantId,
        string scope,
        IdempotencyKey key,
        Guid ownerToken,
        int concurrencyVersion,
        int statusCode,
        IReadOnlyDictionary<string, string[]> headers,
        ReadOnlyMemory<byte> responseBody,
        TimeSpan retentionDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically marks an in-flight idempotency record as failed, enabling subsequent retries depending on policy.
    /// </summary>
    /// <param name="tenantId">The unique identifier of the tenant.</param>
    /// <param name="scope">The functional scope of the operation.</param>
    /// <param name="key">The idempotency key.</param>
    /// <param name="ownerToken">The ownership token issued during key acquisition.</param>
    /// <param name="concurrencyVersion">The concurrency version issued during key acquisition.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains <see langword="true"/> if the record was successfully
    /// marked as failed; otherwise, <see langword="false"/>.
    /// </returns>
    Task<bool> MarkFailedAsync(
        Guid tenantId,
        string scope,
        IdempotencyKey key,
        Guid ownerToken,
        int concurrencyVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes expired completed idempotency records in batches to reclaim storage.
    /// </summary>
    /// <param name="utcNow">The current UTC timestamp threshold for expiration.</param>
    /// <param name="batchSize">The maximum number of records to purge per execution batch.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains the number of expired records permanently purged.
    /// </returns>
    Task<int> CleanupExpiredRecordsAsync(
        DateTimeOffset utcNow,
        int batchSize,
        CancellationToken cancellationToken = default);
}
