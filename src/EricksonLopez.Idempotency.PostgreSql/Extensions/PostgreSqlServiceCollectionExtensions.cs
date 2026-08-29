// Copyright © Erickson Lopez. MIT License.
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EricksonLopez.Idempotency.PostgreSql;

/// <summary>
/// Provides extension methods for <see cref="IServiceCollection"/> to register PostgreSQL idempotency persistence store.
/// </summary>
public static class PostgreSqlServiceCollectionExtensions
{
    /// <summary>
    /// Registers the PostgreSQL implementation of <see cref="IIdempotencyStore"/> in the specified service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the store to.</param>
    /// <returns>The same service collection instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddPostgreSqlIdempotencyStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<PostgreSqlIdempotencyStore>();
        services.TryAddSingleton<IIdempotencyStore>(sp => sp.GetRequiredService<PostgreSqlIdempotencyStore>());
        services.TryAddSingleton<ITransactionalIdempotencyStore>(sp => sp.GetRequiredService<PostgreSqlIdempotencyStore>());
        return services;
    }
}
