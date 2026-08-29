// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Idempotency;

/// <summary>
/// Encapsulates the execution context of the currently executing idempotent request.
/// </summary>
/// <remarks>
/// An instance is populated by the idempotency infrastructure before invoking the handler
/// and is accessible via <see cref="IIdempotencyContextAccessor"/> during execution.
/// </remarks>
public sealed class IdempotencyContext
{
    /// <summary>
    /// Gets or sets the tenant identifier that partitions idempotency records for the current operation.
    /// </summary>
    public Guid TenantId { get; set; } = Guid.Empty;

    /// <summary>
    /// Gets or sets the unique idempotency key identifying the current operation.
    /// </summary>
    public IdempotencyKey? Key { get; set; }

    /// <summary>
    /// Gets or sets the functional scope or logical partition that isolates this operation's idempotency records.
    /// </summary>
    public string Scope { get; set; } = "default";

    /// <summary>
    /// Gets or sets the ownership token issued when this instance successfully acquired the idempotency lease.
    /// </summary>
    /// <remarks><see langword="null"/> when the current execution is a replay of a cached response.</remarks>
    public Guid? OwnerToken { get; set; }

    /// <summary>
    /// Gets or sets the fencing token version number issued with the ownership lease to detect zombie workers.
    /// </summary>
    /// <remarks><see langword="null"/> when the current execution is a replay of a cached response.</remarks>
    public int? ConcurrencyVersion { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the handler is being invoked as part of a cached response replay.
    /// </summary>
    public bool IsReplay { get; set; }
}
