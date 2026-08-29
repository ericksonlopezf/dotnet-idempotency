// Copyright © Erickson Lopez. MIT License.
using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EricksonLopez.Idempotency.AspNetCore;

/// <summary>
/// Provides extension methods for <see cref="IServiceCollection"/>, <see cref="IApplicationBuilder"/>, and endpoint route builders to configure ASP.NET Core idempotency.
/// </summary>
public static class AspNetCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers ASP.NET Core endpoint filters, middleware dependencies, and core idempotency services in the specified service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="configure">An optional delegate to configure <see cref="IdempotencyOptions"/>.</param>
    /// <returns>The same service collection instance for method chaining.</returns>
    public static IServiceCollection AddAspNetCoreIdempotency(
        this IServiceCollection services,
        Action<IdempotencyOptions>? configure = null)
    {
        services.AddIdempotencyCore(configure);
        services.TryAddScoped<IdempotentEndpointFilter>();

        return services;
    }

    /// <summary>
    /// Enables the idempotency middleware in the ASP.NET Core HTTP request pipeline.
    /// </summary>
    /// <param name="app">The <see cref="IApplicationBuilder"/> to configure.</param>
    /// <returns>The same application builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/></exception>
    public static IApplicationBuilder UseIdempotency(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<IdempotencyMiddleware>();
    }

    /// <summary>
    /// Adds idempotency enforcement filter to a mapped route endpoint.
    /// </summary>
    /// <param name="builder">The <see cref="RouteHandlerBuilder"/> to configure.</param>
    /// <returns>The same route handler builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/></exception>
    public static RouteHandlerBuilder WithIdempotency(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddEndpointFilter<IdempotentEndpointFilter>();
        return builder;
    }

    /// <summary>
    /// Adds idempotency enforcement filter to a mapped route group.
    /// </summary>
    /// <param name="builder">The <see cref="RouteGroupBuilder"/> to configure.</param>
    /// <returns>The same route group builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/></exception>
    public static RouteGroupBuilder WithIdempotency(this RouteGroupBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddEndpointFilter<IdempotentEndpointFilter>();
        return builder;
    }
}
