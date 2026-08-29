// Copyright © Erickson Lopez. MIT License.
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EricksonLopez.Idempotency.Oracle;

/// <summary>
/// Provides extension methods for <see cref="IServiceCollection"/> to register Oracle Database idempotency persistence store.
/// </summary>
public static class OracleServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Oracle Database implementation of <see cref="IIdempotencyStore"/> in the specified service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the store to.</param>
    /// <param name="connectionString">The Oracle database connection string.</param>
    /// <returns>The same service collection instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is <see langword="null"/>, empty, or consists only of white-space characters</exception>
    public static IServiceCollection AddOracleIdempotencyStore(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddSingleton(sp => new OracleIdempotencyStore(connectionString));
        services.TryAddSingleton<IIdempotencyStore>(sp => sp.GetRequiredService<OracleIdempotencyStore>());
        services.TryAddSingleton<ITransactionalIdempotencyStore>(sp => sp.GetRequiredService<OracleIdempotencyStore>());
        return services;
    }
}
