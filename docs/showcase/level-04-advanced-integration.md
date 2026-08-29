# Level 04: Advanced Ecosystem Integration (Result, Mediator & Outbox)

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Overview

Level 04 demonstrates how `EricksonLopez.Idempotency` seamlessly integrates with Clean Architecture building blocks:
- **`EricksonLopez.Result`**: Functional error modeling for Railway-Oriented Programming.
- **`EricksonLopez.Mediator`**: Zero-allocation pipeline behavior guarding command handlers.
- **`ITransactionalIdempotencyStore`**: Transactional participation for the Outbox + Idempotency pattern.

---

## 2. Functional Error Modeling with EricksonLopez.Result

The `EricksonLopez.Idempotency.Result` package provides pre-configured domain error factories:

```csharp
using EricksonLopez.Idempotency.Result;
using EricksonLopez.Result;

// 1. In-flight 409 conflict error
Error conflictError = IdempotencyErrors.InFlightConflict("TX-001");
// Code: "Idempotency.InFlightConflict", Type: ErrorType.Conflict

// 2. Payload fingerprint mismatch 409 error
Error mismatchError = IdempotencyErrors.FingerprintMismatch("TX-001");
// Code: "Idempotency.FingerprintMismatch", Type: ErrorType.Validation

// 3. Fencing token / lease expiration error
Error leaseLostError = IdempotencyErrors.LeaseLost("TX-001");
// Code: "Idempotency.LeaseLost", Type: ErrorType.Conflict
```

---

## 3. Mediator Pipeline Behavior Integration

The `EricksonLopez.Idempotency.Mediator` package provides `IdempotencyPipelineBehavior<TRequest, TResponse>`:

```csharp
using System;
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.Mediator;
using EricksonLopez.Mediator;

// 1. Mark command as idempotent
public sealed record TransferFundsCommand(string FromAccount, string ToAccount, decimal Amount, string Key) 
    : IIdempotentRequest, IRequest<TransferFundsResult>
{
    public IdempotencyKey IdempotencyKey => new IdempotencyKey(Key);

    // Optional multi-tenant partition identifier
    public Guid TenantId => Guid.Empty;
}

public sealed record TransferFundsResult(string TransactionId, decimal Amount);

// 2. Register pipeline behavior in DI
services.AddMediator(cfg =>
{
    cfg.AddOpenBehavior(typeof(IdempotencyPipelineBehavior<,>));
});
```

---

## 4. Transactional Store Participation (Outbox + Idempotency)

When executing an Outbox event write and domain entity update, the idempotency record update can participate in the **same database transaction** via `ITransactionalIdempotencyStore`:

```csharp
await using var conn = await dataSource.OpenConnectionAsync(ct);
await using var tx = await conn.BeginTransactionAsync(ct);

try
{
    // 1. Domain logic
    await orderRepository.SaveAsync(order, conn, tx, ct);

    // 2. Outbox event
    await outbox.EnqueueAsync(new OrderCreatedEvent(order.Id), conn, tx, ct);

    // 3. Mark idempotency as completed in SAME transaction
    if (store is ITransactionalIdempotencyStore txStore)
    {
        await txStore.MarkCompletedAsync(
            tenantId, "orders", key,
            claim.OwnerToken!.Value, claim.ConcurrencyVersion!.Value,
            200, headers, serializedResponse, TimeSpan.FromDays(7),
            conn, tx, ct);
    }

    // Atomic commit of all three operations!
    await tx.CommitAsync(ct);
}
catch
{
    await tx.RollbackAsync(ct);
    await store.MarkFailedAsync(tenantId, "orders", key, claim.OwnerToken!.Value, claim.ConcurrencyVersion!.Value, CancellationToken.None);
    throw;
}
```

---

## 5. Next Steps

Proceed to [Level 05: High Concurrency & Race Conditions](level-05-high-concurrency.md).
