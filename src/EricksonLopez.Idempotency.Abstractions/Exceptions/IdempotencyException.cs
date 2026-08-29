// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Idempotency.Exceptions;

/// <summary>
/// Represents the base exception for errors originating within the idempotency framework.
/// </summary>
public class IdempotencyException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyException"/> class.
    /// </summary>
    public IdempotencyException() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public IdempotencyException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyException"/> class with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public IdempotencyException(string message, Exception innerException) : base(message, innerException) { }
}
