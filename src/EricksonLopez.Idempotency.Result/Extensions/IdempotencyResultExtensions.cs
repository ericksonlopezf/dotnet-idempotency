// Copyright © Erickson Lopez. MIT License.
using EricksonLopez.Result;

namespace EricksonLopez.Idempotency.Result;

/// <summary>
/// Provides extension methods for <see cref="IdempotencyClaimResult"/> to convert claim outcomes into functional <see cref="Result{T}"/> instances.
/// </summary>
public static class IdempotencyResultExtensions
{
    /// <summary>
    /// Converts a conflicted or mismatched claim result into a corresponding failure <see cref="Result{T}"/>.
    /// </summary>
    /// <typeparam name="T">The result payload type.</typeparam>
    /// <param name="claimResult">The claim outcome to evaluate.</param>
    /// <param name="key">The idempotency key string.</param>
    /// <returns>
    /// A failure <see cref="Result{T}"/> if the claim encountered a conflict or mismatch; otherwise, <see langword="null"/>.
    /// </returns>
    public static Result<T>? AsErrorResult<T>(this IdempotencyClaimResult claimResult, string key)
    {
        if (claimResult.Status == ClaimResultStatus.FingerprintMismatch)
        {
            return Result<T>.Failure(IdempotencyErrors.FingerprintMismatch(key));
        }

        if (claimResult.Status == ClaimResultStatus.InFlightConflict)
        {
            return Result<T>.Failure(IdempotencyErrors.InFlightConflict(key));
        }

        return null;
    }
}
