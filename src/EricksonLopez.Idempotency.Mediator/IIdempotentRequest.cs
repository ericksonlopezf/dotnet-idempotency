// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Idempotency.Mediator;

/// <summary>
/// Defines a contract for requests in the Mediator pipeline that require deterministic idempotency enforcement.
/// </summary>
public interface IIdempotentRequest
{
    /// <summary>
    /// Gets the unique idempotency key associated with this request.
    /// </summary>
    IdempotencyKey IdempotencyKey { get; }

    /// <summary>
    /// Gets the tenant identifier for this request.
    /// </summary>
    /// <remarks>
    /// In multi-tenant systems, this returns the current tenant's <see cref="Guid"/>.
    /// In single-tenant systems, returns <see cref="Guid.Empty"/>.
    /// The tenant identifier is included in the idempotency composite key <c>(TenantId, Scope, Key)</c>
    /// to guarantee per-tenant isolation of idempotency records.
    /// </remarks>
    Guid TenantId { get; }
}
