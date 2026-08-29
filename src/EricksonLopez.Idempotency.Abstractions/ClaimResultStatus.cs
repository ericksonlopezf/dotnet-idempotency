// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Idempotency;

/// <summary>
/// Defines the specific outcome of an attempt to acquire ownership or claim an idempotency key.
/// </summary>
public enum ClaimResultStatus : byte
{
    /// <summary>
    /// Specifies that the key did not exist previously and ownership was successfully acquired as a new operation.
    /// </summary>
    AcquiredNew = 1,

    /// <summary>
    /// Specifies that the key existed but was stale due to an expired lease or failure, and ownership was reclaimed.
    /// </summary>
    AcquiredStale = 2,

    /// <summary>
    /// Specifies that the operation was already completed previously and the stored cached response should be replayed.
    /// </summary>
    CompletedReplay = 3,

    /// <summary>
    /// Specifies that the operation is currently being executed by another concurrent worker under an active lease.
    /// </summary>
    InFlightConflict = 4,

    /// <summary>
    /// Specifies that the key was previously used with a different request payload or cryptographic fingerprint.
    /// </summary>
    FingerprintMismatch = 5
}
