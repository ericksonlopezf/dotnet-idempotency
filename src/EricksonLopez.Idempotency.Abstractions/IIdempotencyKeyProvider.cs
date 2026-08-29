// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Idempotency;

/// <summary>
/// Defines a strategy for resolving an idempotency key from an incoming request or message context.
/// </summary>
/// <typeparam name="TContext">The request or execution context type.</typeparam>
public interface IIdempotencyKeyProvider<in TContext>
{
    /// <summary>
    /// Attempts to resolve an idempotency key from the specified context.
    /// </summary>
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A value task representing the asynchronous operation. The task result contains the resolved
    /// <see cref="IdempotencyKey"/>, or <see langword="null"/> if none was found.
    /// </returns>
    ValueTask<IdempotencyKey?> TryGetKeyAsync(TContext context, CancellationToken cancellationToken = default);
}
