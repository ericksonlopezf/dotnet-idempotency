// Copyright © Erickson Lopez. MIT License.
using System;
using Microsoft.AspNetCore.Http;

namespace EricksonLopez.Idempotency.AspNetCore;

/// <summary>
/// Provides ASP.NET Core specific extension methods for configuring <see cref="IdempotencyOptions"/>.
/// </summary>
public static class IdempotencyOptionsAspNetCoreExtensions
{
    /// <summary>
    /// Configures a typed <see cref="HttpContext"/> extractor delegate for resolving tenant identifiers.
    /// </summary>
    /// <param name="options">The idempotency options instance.</param>
    /// <param name="extractor">The delegate that resolves a <see cref="Guid"/> tenant identifier from the current <see cref="HttpContext"/>.</param>
    /// <returns>The same <see cref="IdempotencyOptions"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or <paramref name="extractor"/> is <see langword="null"/></exception>
    public static IdempotencyOptions UseTenantIdExtractor(
        this IdempotencyOptions options,
        Func<HttpContext, Guid> extractor)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(extractor);

        options.TenantIdExtractor = contextObj =>
            contextObj is HttpContext httpContext
                ? extractor(httpContext)
                : Guid.Empty;

        return options;
    }
}
