// Copyright © Erickson Lopez. MIT License.
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace EricksonLopez.Idempotency.Redis;

/// <summary>
/// Provides extension methods for <see cref="IServiceCollection"/> to register the Redis idempotency store.
/// </summary>
public static class RedisServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="RedisIdempotencyStore"/> as the <see cref="IIdempotencyStore"/> implementation,
    /// using an existing <see cref="IConnectionMultiplexer"/> registered in the DI container.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="configure">An optional delegate to configure <see cref="RedisIdempotencyOptions"/>.</param>
    /// <returns>The same service collection instance for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// Ensure that <see cref="IConnectionMultiplexer"/> is already registered before calling this method.
    /// If using <c>StackExchange.Redis.Extensions</c> or similar, this is typically handled by the respective package.
    /// </para>
    /// <para>
    /// For manual registration:
    /// <code>
    /// services.AddSingleton&lt;IConnectionMultiplexer&gt;(_ =>
    ///     ConnectionMultiplexer.Connect("localhost:6379"));
    /// services.AddIdempotencyCore();
    /// services.AddRedisIdempotency();
    /// </code>
    /// </para>
    /// <para>
    /// <b>Important</b>: The Redis provider offers slightly weaker correctness guarantees than the SQL
    /// providers due to the absence of true fencing tokens. For critical financial operations, prefer
    /// the SQL providers (PostgreSQL, SQL Server). See ADR-013 for the full analysis.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddRedisIdempotency(
        this IServiceCollection services,
        Action<RedisIdempotencyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new RedisIdempotencyOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IIdempotencyStore, RedisIdempotencyStore>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="RedisIdempotencyStore"/> as the <see cref="IIdempotencyStore"/> implementation,
    /// using a connection string to create and register a new <see cref="IConnectionMultiplexer"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="connectionString">The Redis connection string (e.g., <c>"localhost:6379"</c>).</param>
    /// <param name="configure">An optional delegate to configure <see cref="RedisIdempotencyOptions"/>.</param>
    /// <returns>The same service collection instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="connectionString"/> is <see langword="null"/></exception>
    public static IServiceCollection AddRedisIdempotency(
        this IServiceCollection services,
        string connectionString,
        Action<RedisIdempotencyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionString);

        services.TryAddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(connectionString));

        return services.AddRedisIdempotency(configure);
    }
}
