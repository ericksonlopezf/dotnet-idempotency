// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Idempotency;

/// <summary>
/// Provides the default implementation of <see cref="IIdempotencyPolicy"/> based on configured options.
/// </summary>
public sealed class DefaultIdempotencyPolicy : IIdempotencyPolicy
{
    private readonly IdempotencyOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultIdempotencyPolicy"/> class with the specified idempotency options.
    /// </summary>
    /// <param name="options">The idempotency configuration options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/></exception>
    public DefaultIdempotencyPolicy(IdempotencyOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public TimeSpan LeaseDuration => _options.DefaultLeaseDuration;

    /// <inheritdoc />
    public TimeSpan RetentionDuration => _options.DefaultRetentionDuration;

    /// <inheritdoc />
    public bool AllowRetryOnFailure => true;

    /// <inheritdoc />
    public bool IsCacheableStatusCode(int statusCode)
    {
        // Cache only successful responses (HTTP 200-299).
        return statusCode is >= 200 and < 300;
    }
}
