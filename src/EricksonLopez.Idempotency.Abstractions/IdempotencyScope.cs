// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Idempotency;

/// <summary>
/// Represents the logical partition or functional boundary of an idempotent operation (e.g. endpoint route or command type).
/// </summary>
/// <remarks>
/// This type encapsulates a non-empty scope string of up to 64 characters.
/// Comparison is performed case-insensitively using ordinal rules.
/// </remarks>
public readonly record struct IdempotencyScope : IEquatable<IdempotencyScope>, IComparable<IdempotencyScope>, IComparable
{
    /// <summary>
    /// Gets the default fallback scope used when no specific scope is supplied.
    /// </summary>
    public static readonly IdempotencyScope Default = new("default");

    /// <summary>
    /// Gets the underlying string value representing the scope.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyScope"/> struct.
    /// </summary>
    /// <param name="value">The scope string identifying the functional partition.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> exceeds 64 characters</exception>
    public IdempotencyScope(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Idempotency scope cannot exceed 64 characters.");
        }
        Value = value;
    }

    /// <summary>
    /// Creates a new <see cref="IdempotencyScope"/> instance after validating input invariants.
    /// </summary>
    /// <param name="value">The scope string.</param>
    /// <returns>A validated <see cref="IdempotencyScope"/> instance.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> exceeds 64 characters</exception>
    public static IdempotencyScope Create(string value) => new(value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <inheritdoc />
    public int CompareTo(IdempotencyScope other) => string.Compare(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="obj"/> is not of type <see cref="IdempotencyScope"/></exception>
    public int CompareTo(object? obj)
    {
        if (obj is null) return 1;
        if (obj is IdempotencyScope other) return CompareTo(other);
        throw new ArgumentException("Object must be of type IdempotencyScope", nameof(obj));
    }

    /// <summary>
    /// Determines whether a specified <see cref="IdempotencyScope"/> is less than another specified <see cref="IdempotencyScope"/>.
    /// </summary>
    /// <param name="left">The first <see cref="IdempotencyScope"/> to compare.</param>
    /// <param name="right">The second <see cref="IdempotencyScope"/> to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is less than <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator <(IdempotencyScope left, IdempotencyScope right) => left.CompareTo(right) < 0;

    /// <summary>
    /// Determines whether a specified <see cref="IdempotencyScope"/> is less than or equal to another specified <see cref="IdempotencyScope"/>.
    /// </summary>
    /// <param name="left">The first <see cref="IdempotencyScope"/> to compare.</param>
    /// <param name="right">The second <see cref="IdempotencyScope"/> to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is less than or equal to <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator <=(IdempotencyScope left, IdempotencyScope right) => left.CompareTo(right) <= 0;

    /// <summary>
    /// Determines whether a specified <see cref="IdempotencyScope"/> is greater than another specified <see cref="IdempotencyScope"/>.
    /// </summary>
    /// <param name="left">The first <see cref="IdempotencyScope"/> to compare.</param>
    /// <param name="right">The second <see cref="IdempotencyScope"/> to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is greater than <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator >(IdempotencyScope left, IdempotencyScope right) => left.CompareTo(right) > 0;

    /// <summary>
    /// Determines whether a specified <see cref="IdempotencyScope"/> is greater than or equal to another specified <see cref="IdempotencyScope"/>.
    /// </summary>
    /// <param name="left">The first <see cref="IdempotencyScope"/> to compare.</param>
    /// <param name="right">The second <see cref="IdempotencyScope"/> to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is greater than or equal to <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator >=(IdempotencyScope left, IdempotencyScope right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Converts an <see cref="IdempotencyScope"/> to its underlying <see cref="string"/> representation.
    /// </summary>
    /// <param name="scope">The <see cref="IdempotencyScope"/> to convert.</param>
    /// <returns>The underlying string representation of the scope.</returns>
    public static implicit operator string(IdempotencyScope scope) => scope.Value;

    /// <summary>
    /// Converts a <see cref="string"/> to an <see cref="IdempotencyScope"/> instance.
    /// </summary>
    /// <param name="value">The string value to convert.</param>
    /// <returns>A new <see cref="IdempotencyScope"/> initialized with the specified value.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> exceeds 64 characters</exception>
    public static explicit operator IdempotencyScope(string value) => new(value);
}
