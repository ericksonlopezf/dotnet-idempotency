// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics.CodeAnalysis;

namespace EricksonLopez.Idempotency;

/// <summary>
/// Represents an immutable, strongly-typed idempotency key value object.
/// </summary>
/// <remarks>
/// This type encapsulates a trimmed, non-empty key string of up to 128 characters.
/// Value equality and ordinal comparison are supported.
/// </remarks>
public readonly record struct IdempotencyKey : IEquatable<IdempotencyKey>, IComparable<IdempotencyKey>, IComparable
{
    private readonly string? _value;

    /// <summary>
    /// Represents an empty or uninitialized idempotency key.
    /// </summary>
    public static readonly IdempotencyKey Empty;

    /// <summary>
    /// Gets the string representation of the idempotency key, or an empty string if uninitialized.
    /// </summary>
    public string Value => _value ?? string.Empty;

    /// <summary>
    /// Gets a value indicating whether this idempotency key is empty or uninitialized.
    /// </summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(_value);

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyKey"/> struct.
    /// </summary>
    /// <param name="value">The unique string key supplied by the client or producer.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> exceeds 128 characters</exception>
    public IdempotencyKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var trimmed = value.Trim();
        if (trimmed.Length > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Idempotency key cannot exceed 128 characters.");
        }
        _value = trimmed;
    }

    /// <summary>
    /// Creates a new <see cref="IdempotencyKey"/> instance after validating input invariants.
    /// </summary>
    /// <param name="value">The unique string key.</param>
    /// <returns>A validated <see cref="IdempotencyKey"/> instance.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> exceeds 128 characters</exception>
    public static IdempotencyKey Create(string value) => new(value);

    /// <summary>
    /// Creates a new <see cref="IdempotencyKey"/> from the specified <see cref="Guid"/>.
    /// </summary>
    /// <param name="identifier">The unique identifier.</param>
    /// <returns>A new <see cref="IdempotencyKey"/> instance initialized with the hyphenless string representation of the identifier.</returns>
    /// <exception cref="ArgumentException"><paramref name="identifier"/> is <see cref="Guid.Empty"/></exception>
    public static IdempotencyKey Create(Guid identifier)
    {
        if (identifier == Guid.Empty)
        {
            throw new ArgumentException("Idempotency key cannot be created from Guid.Empty.", nameof(identifier));
        }

        return new IdempotencyKey(identifier.ToString("N"));
    }

    /// <summary>
    /// Generates a new random <see cref="IdempotencyKey"/> using a cryptographically unique identifier.
    /// </summary>
    /// <returns>A new <see cref="IdempotencyKey"/> initialized with a newly generated identifier.</returns>
    public static IdempotencyKey NewKey() => new(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Attempts to parse a string into an <see cref="IdempotencyKey"/>.
    /// </summary>
    /// <param name="candidate">The string candidate to parse.</param>
    /// <param name="key">
    /// When this method returns, contains the parsed <see cref="IdempotencyKey"/> if parsing succeeded;
    /// otherwise, the default uninitialized value.
    /// </param>
    /// <returns><see langword="true"/> if the string was successfully parsed; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse([NotNullWhen(true)] string? candidate, out IdempotencyKey key)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            key = default;
            return false;
        }

        var trimmed = candidate.Trim();
        if (trimmed.Length > 128)
        {
            key = default;
            return false;
        }

        key = new IdempotencyKey(trimmed);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <inheritdoc />
    public int CompareTo(IdempotencyKey other) => string.Compare(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="obj"/> is not of type <see cref="IdempotencyKey"/></exception>
    public int CompareTo(object? obj)
    {
        if (obj is null) return 1;
        if (obj is IdempotencyKey other) return CompareTo(other);
        throw new ArgumentException("Object must be of type IdempotencyKey", nameof(obj));
    }

    /// <summary>
    /// Determines whether a specified <see cref="IdempotencyKey"/> is less than another specified <see cref="IdempotencyKey"/>.
    /// </summary>
    /// <param name="left">The first <see cref="IdempotencyKey"/> to compare.</param>
    /// <param name="right">The second <see cref="IdempotencyKey"/> to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is less than <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator <(IdempotencyKey left, IdempotencyKey right) => left.CompareTo(right) < 0;

    /// <summary>
    /// Determines whether a specified <see cref="IdempotencyKey"/> is less than or equal to another specified <see cref="IdempotencyKey"/>.
    /// </summary>
    /// <param name="left">The first <see cref="IdempotencyKey"/> to compare.</param>
    /// <param name="right">The second <see cref="IdempotencyKey"/> to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is less than or equal to <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator <=(IdempotencyKey left, IdempotencyKey right) => left.CompareTo(right) <= 0;

    /// <summary>
    /// Determines whether a specified <see cref="IdempotencyKey"/> is greater than another specified <see cref="IdempotencyKey"/>.
    /// </summary>
    /// <param name="left">The first <see cref="IdempotencyKey"/> to compare.</param>
    /// <param name="right">The second <see cref="IdempotencyKey"/> to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is greater than <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator >(IdempotencyKey left, IdempotencyKey right) => left.CompareTo(right) > 0;

    /// <summary>
    /// Determines whether a specified <see cref="IdempotencyKey"/> is greater than or equal to another specified <see cref="IdempotencyKey"/>.
    /// </summary>
    /// <param name="left">The first <see cref="IdempotencyKey"/> to compare.</param>
    /// <param name="right">The second <see cref="IdempotencyKey"/> to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is greater than or equal to <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator >=(IdempotencyKey left, IdempotencyKey right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Converts an <see cref="IdempotencyKey"/> to its underlying <see cref="string"/> representation.
    /// </summary>
    /// <param name="key">The <see cref="IdempotencyKey"/> to convert.</param>
    /// <returns>The underlying string representation of the key.</returns>
    public static implicit operator string(IdempotencyKey key) => key.Value;

    /// <summary>
    /// Converts a <see cref="string"/> to an <see cref="IdempotencyKey"/> instance.
    /// </summary>
    /// <param name="value">The string value to convert.</param>
    /// <returns>A new <see cref="IdempotencyKey"/> initialized with the specified value.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> exceeds 128 characters</exception>
    public static explicit operator IdempotencyKey(string value) => new(value);
}
