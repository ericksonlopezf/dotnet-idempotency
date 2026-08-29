// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Idempotency.Exceptions;

/// <summary>
/// Represents an exception thrown when an operation discovers its ownership lease has expired and could not be finalized.
/// </summary>
public sealed class IdempotencyLeaseExpiredException : IdempotencyException
{
    /// <summary>
    /// Gets the idempotency key whose lease expired.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyLeaseExpiredException"/> class with the specified idempotency key.
    /// </summary>
    /// <param name="key">The idempotency key.</param>
    public IdempotencyLeaseExpiredException(string key)
        : base($"The ownership lease for idempotency key '{key}' has expired and could not be finalized.")
    {
        Key = key;
    }
}
