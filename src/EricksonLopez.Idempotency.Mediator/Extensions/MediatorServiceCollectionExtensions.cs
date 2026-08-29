// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EricksonLopez.Idempotency.Mediator;

/// <summary>
/// Provides extension methods for <see cref="IServiceCollection"/> to register Mediator idempotency pipeline behaviors.
/// </summary>
public static class MediatorServiceCollectionExtensions
{
    /// <summary>
    /// Registers the open generic <see cref="IdempotencyPipelineBehavior{TRequest, TResponse}"/> and core idempotency dependencies in the specified service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <returns>The same service collection instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddMediatorIdempotency(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddIdempotencyCore();
        services.TryAddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotencyPipelineBehavior<,>));

        return services;
    }
}
