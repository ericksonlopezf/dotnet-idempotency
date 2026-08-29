// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Idempotency.Exceptions;

/// <summary>
/// Represents an exception thrown when an in-flight execution conflict occurs because an identical operation is actively executing.
/// </summary>
public sealed class IdempotencyConflictException : IdempotencyException
{
    /// <summary>
    /// Gets the conflicting idempotency key.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyConflictException"/> class with the specified conflicting idempotency key.
    /// </summary>
    /// <param name="key">The conflicting idempotency key.</param>
    public IdempotencyConflictException(string key)
        : base($"An identical operation with idempotency key '{key}' is currently in-flight and executing.")
    {
        Key = key;
    }
}
