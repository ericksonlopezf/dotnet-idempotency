// Copyright © Erickson Lopez. MIT License.
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EricksonLopez.Idempotency.MariaDb;

/// <summary>
/// Provides extension methods for <see cref="IServiceCollection"/> to register MariaDB idempotency persistence store.
/// </summary>
public static class MariaDbServiceCollectionExtensions
{
    /// <summary>
    /// Registers the MariaDB implementation of <see cref="IIdempotencyStore"/> in the specified service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the store to.</param>
    /// <returns>The same service collection instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddMariaDbIdempotencyStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<MariaDbIdempotencyStore>();
        services.TryAddSingleton<IIdempotencyStore>(sp => sp.GetRequiredService<MariaDbIdempotencyStore>());
        return services;
    }
}
