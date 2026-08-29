// Copyright © Erickson Lopez. MIT License.
using EricksonLopez.Result;

namespace EricksonLopez.Idempotency.Result;

/// <summary>
/// Provides domain error factory methods for idempotency failures when utilizing the Result pattern.
/// </summary>
public static class IdempotencyErrors
{
    /// <summary>
    /// Creates a conflict error indicating that an operation with the same key is actively executing.
    /// </summary>
    /// <param name="key">The conflicting idempotency key.</param>
    /// <returns>A standardized <see cref="Error"/> representing the conflict.</returns>
    public static Error InFlightConflict(string key) =>
        Error.Conflict(
            code: "Idempotency.InFlightConflict",
            description: $"An identical operation with idempotency key '{key}' is currently being processed.");

    /// <summary>
    /// Creates a validation error indicating that a key was reused with conflicting payload parameters.
    /// </summary>
    /// <param name="key">The mismatched idempotency key.</param>
    /// <returns>A standardized <see cref="Error"/> representing the validation mismatch.</returns>
    public static Error FingerprintMismatch(string key) =>
        Error.Validation(
            code: "Idempotency.FingerprintMismatch",
            description: $"The idempotency key '{key}' was previously used with a different request payload.");

    /// <summary>
    /// Creates a failure error indicating that an ownership lease was lost or expired before completion.
    /// </summary>
    /// <param name="key">The idempotency key.</param>
    /// <returns>A standardized <see cref="Error"/> representing the lost lease.</returns>
    public static Error LeaseLost(string key) =>
        Error.Failure(
            code: "Idempotency.LeaseLost",
            description: $"Ownership lease for idempotency key '{key}' was lost before completion.");
}
