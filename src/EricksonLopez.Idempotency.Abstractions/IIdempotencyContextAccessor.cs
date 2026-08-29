// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Idempotency;

/// <summary>
/// Provides access to the current <see cref="IdempotencyContext"/>.
/// </summary>
public interface IIdempotencyContextAccessor
{
    /// <summary>
    /// Gets or sets the current <see cref="IdempotencyContext"/>.
    /// </summary>
    IdempotencyContext? IdempotencyContext { get; set; }
}
