// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Idempotency.AspNetCore;

/// <summary>
/// Specifies that an endpoint or controller action requires idempotency enforcement.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class IdempotentAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a custom scope identifier that overrides the default request path scope for idempotency key isolation.
    /// </summary>
    /// <remarks>When <see langword="null"/>, the request path is used as the scope.</remarks>
    public string? Scope { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the idempotency key header is strictly required.
    /// </summary>
    /// <remarks>
    /// When <see langword="true"/> (the default), requests without the idempotency key header will receive
    /// an HTTP 400 Bad Request response. When <see langword="false"/>, requests without the header are
    /// processed normally without idempotency enforcement.
    /// </remarks>
    public bool Required { get; set; } = true;

    /// <summary>
    /// Gets or sets the duration in seconds for which the in-flight execution lease is held before it may be reclaimed.
    /// </summary>
    public int LeaseDurationSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the number of days for which completed idempotency records are retained before becoming eligible for cleanup.
    /// </summary>
    public int RetentionDurationDays { get; set; } = 7;

    /// <summary>
    /// Gets or sets a value indicating whether idempotency enforcement is active for this endpoint.
    /// </summary>
    /// <remarks>
    /// Set to <see langword="false"/> to opt a specific endpoint out of idempotency enforcement
    /// even when the middleware or filter is globally registered. This is useful for endpoints
    /// that are individually decorated with <see cref="IdempotentAttribute"/> but belong to a
    /// group with idempotency configured at the group level.
    /// </remarks>
    public bool Enabled { get; set; } = true;
}
