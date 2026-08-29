// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Idempotency;

/// <summary>
/// Defines a strategy for computing deterministic fingerprints from request components.
/// </summary>
public interface IIdempotencyFingerprintGenerator
{
    /// <summary>
    /// Computes a deterministic hexadecimal fingerprint string from the specified operation components.
    /// </summary>
    /// <remarks>
    /// Implementations are free to choose any deterministic hashing algorithm (SHA-256, HMAC-SHA256, etc.).
    /// The default implementation uses SHA-256 and returns an uppercase hexadecimal string.
    /// </remarks>
    /// <param name="operationName">The logical operation or endpoint route name.</param>
    /// <param name="scope">The functional partition scope.</param>
    /// <param name="tenantId">The string tenant identifier.</param>
    /// <param name="authenticatedSubject">The authenticated subject or user identifier if present; otherwise, <see langword="null"/>.</param>
    /// <param name="payloadBytes">The raw payload bytes to hash.</param>
    /// <returns>An uppercase hexadecimal hash string representing the request fingerprint.</returns>
    string GenerateFingerprint(
        string operationName,
        string scope,
        string tenantId,
        string? authenticatedSubject,
        ReadOnlySpan<byte> payloadBytes);
}
