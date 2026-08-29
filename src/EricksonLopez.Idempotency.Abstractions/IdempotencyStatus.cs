// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Idempotency;

/// <summary>
/// Defines the lifecycle execution state of an idempotency record.
/// </summary>
public enum IdempotencyStatus : byte
{
    /// <summary>
    /// Specifies that the operation is currently being executed under an active lease.
    /// </summary>
    Processing = 1,

    /// <summary>
    /// Specifies that the operation was completed successfully and the result payload is immutable and cached.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Specifies that the operation failed during execution and may be eligible for retry depending on policy.
    /// </summary>
    Failed = 3
}
