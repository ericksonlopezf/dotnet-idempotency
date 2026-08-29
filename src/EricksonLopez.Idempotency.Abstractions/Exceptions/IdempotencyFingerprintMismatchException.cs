// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Idempotency.Exceptions;

/// <summary>
/// Represents an exception thrown when an idempotency key is reused with a different request payload or cryptographic fingerprint.
/// </summary>
public sealed class IdempotencyFingerprintMismatchException : IdempotencyException
{
    /// <summary>
    /// Gets the reused idempotency key.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the original recorded fingerprint.
    /// </summary>
    public string? ExpectedFingerprint { get; }

    /// <summary>
    /// Gets the fingerprint generated for the incoming request.
    /// </summary>
    public string? ActualFingerprint { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyFingerprintMismatchException"/> class with the specified key, expected fingerprint, and actual fingerprint.
    /// </summary>
    /// <param name="key">The idempotency key.</param>
    /// <param name="expectedFingerprint">The recorded fingerprint.</param>
    /// <param name="actualFingerprint">The current request fingerprint.</param>
    public IdempotencyFingerprintMismatchException(string key, string? expectedFingerprint, string? actualFingerprint)
        : base($"Idempotency key '{key}' was reused with mismatched payload parameters (Fingerprint: {actualFingerprint} vs {expectedFingerprint}).")
    {
        Key = key;
        ExpectedFingerprint = expectedFingerprint;
        ActualFingerprint = actualFingerprint;
    }
}
