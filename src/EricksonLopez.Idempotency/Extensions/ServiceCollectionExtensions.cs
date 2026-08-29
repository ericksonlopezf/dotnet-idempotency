// Copyright © Erickson Lopez. MIT License.
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EricksonLopez.Idempotency;

/// <summary>
/// Provides extension methods for <see cref="IServiceCollection"/> to configure core idempotency services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers core idempotency abstractions, context accessors, serializers, and execution engines in the specified service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="configure">An optional delegate to configure <see cref="IdempotencyOptions"/>.</param>
    /// <returns>The same service collection instance for method chaining.</returns>
    public static IServiceCollection AddIdempotencyCore(
        this IServiceCollection services,
        Action<IdempotencyOptions>? configure = null)
    {
        var options = new IdempotencyOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IIdempotencyPolicy, DefaultIdempotencyPolicy>();
        services.TryAddSingleton<IIdempotencyFingerprintGenerator, IdempotencyFingerprintHasher>();
        services.TryAddSingleton<IIdempotencySerializer, SystemTextJsonIdempotencySerializer>();
        services.TryAddSingleton<IIdempotencyContextAccessor, AsyncLocalIdempotencyContextAccessor>();
        services.TryAddScoped<IdempotencyEngine>();

        return services;
    }

    /// <summary>
    /// Registers a hosted background service that periodically purges expired idempotency records from the store.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the service to.</param>
    /// <param name="configure">An optional delegate to configure <see cref="IdempotencyCleanupOptions"/>.</param>
    /// <returns>The same service collection instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    /// <remarks>
    /// <para>
    /// This service must be registered alongside a concrete <see cref="IIdempotencyStore"/> implementation.
    /// Calling this method without a registered store will cause a startup exception.
    /// </para>
    /// <para>
    /// The cleanup service is entirely opt-in and is NOT registered by <see cref="AddIdempotencyCore"/>.
    /// </para>
    /// <example>
    /// <code>
    /// services.AddIdempotencyCore(options => { ... });
    /// services.AddPostgreSqlIdempotency(connectionString);
    /// services.AddIdempotencyCleanupService(cleanup =>
    /// {
    ///     cleanup.Interval = TimeSpan.FromHours(6);
    ///     cleanup.BatchSize = 500;
    /// });
    /// </code>
    /// </example>
    /// </remarks>
    public static IServiceCollection AddIdempotencyCleanupService(
        this IServiceCollection services,
        Action<IdempotencyCleanupOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var cleanupOptions = new IdempotencyCleanupOptions();
        configure?.Invoke(cleanupOptions);

        services.TryAddSingleton(cleanupOptions);
        services.AddHostedService<IdempotencyCleanupBackgroundService>();

        return services;
    }
}
