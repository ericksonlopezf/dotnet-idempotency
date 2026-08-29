// Copyright © Erickson Lopez. MIT License.
using System.Threading;

namespace EricksonLopez.Idempotency;

/// <summary>
/// Provides an implementation of <see cref="IIdempotencyContextAccessor"/> backed by an asynchronous local storage.
/// </summary>
public sealed class AsyncLocalIdempotencyContextAccessor : IIdempotencyContextAccessor
{
    private static readonly AsyncLocal<IdempotencyContextHolder> _currentContext = new();

    /// <inheritdoc />
    public IdempotencyContext? IdempotencyContext
    {
        get => _currentContext.Value?.Context;
        set
        {
            var holder = _currentContext.Value;
            if (holder != null)
            {
                holder.Context = null;
            }

            if (value != null)
            {
                _currentContext.Value = new IdempotencyContextHolder { Context = value };
            }
        }
    }

    private sealed class IdempotencyContextHolder
    {
        public IdempotencyContext? Context;
    }
}
