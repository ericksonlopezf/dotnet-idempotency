# Integration with Database Transactions (Outbox + Idempotency)

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Transactional Boundaries & Critical Failure Windows

There are two dangerous failure windows when idempotency and business transactions interact:

### Failure Window A: False Success
```text
Step 1: Idempotency marked Succeeded
Step 2: Business Transaction Rollback (e.g. database error)
==> The client retries and receives a cached Success response, but the order NEVER existed!
```

### Failure Window B: False Duplicate Execution
```text
Step 1: Business Transaction Committed
Step 2: Idempotency record failed to update (network crash)
==> The client retries and re-executes the business logic, charging the customer twice!
```

---

## 2. Eliminating Failure Windows

`EricksonLopez.Idempotency` eliminates these windows through strict transactional staging:

1. **Step 1 (Outside Transaction)**: Claim key with status `Processing`.
2. **Step 2 (Inside Transaction)**:
   - Perform domain state changes.
   - Insert outbox events.
   - Mark idempotency as completed **within the same transaction** using `ITransactionalIdempotencyStore`.
   - Commit transaction atomically.
3. **Step 3 (If Transaction Rolls Back)**:
   - Mark idempotency record as `Failed`, clearing the lease and enabling safe client retries.

---

## 3. Using ITransactionalIdempotencyStore

SQL-backed stores implement `ITransactionalIdempotencyStore`, which adds overloads for `MarkCompletedAsync` and `MarkFailedAsync` that accept an existing `IDbConnection` and `IDbTransaction?`.

> [!IMPORTANT]
> The standard `IIdempotencyStore.MarkCompletedAsync` always opens its own connection, which means it is **outside** any caller transaction. For true atomicity, always use `ITransactionalIdempotencyStore` when participating in a shared transaction.

### Registration

```csharp
// PostgreSqlIdempotencyStore, SqlServerIdempotencyStore, MySqlIdempotencyStore, and MariaDbIdempotencyStore implement ITransactionalIdempotencyStore.
// Register the store:
services.AddPostgreSqlIdempotencyStore(); // or services.AddSqlServerIdempotencyStore("ConnectionString") / services.AddMySqlIdempotencyStore() / services.AddMariaDbIdempotencyStore()

// In consuming code, resolve as ITransactionalIdempotencyStore:
var txStore = serviceProvider.GetRequiredService<IIdempotencyStore>() as ITransactionalIdempotencyStore;
// OR inject ITransactionalIdempotencyStore directly if you know the provider supports it
```

### Complete Outbox + Idempotency Atomic Pattern

```csharp
public async Task<OrderResult> ExecuteTransactionalOrderAsync(
    Guid tenantId,
    IdempotencyKey key,
    CreateOrderCommand command,
    CancellationToken cancellationToken)
{
    var fingerprint = IdempotencyFingerprintHasher.Compute(
        "CreateOrder", "orders", tenantId.ToString(), null, command.PayloadBytes);

    // Step 1: Claim key BEFORE opening the transaction
    var claim = await _store.TryAcquireAsync(
        tenantId, "orders", key, fingerprint,
        TimeSpan.FromSeconds(30), TimeSpan.FromDays(7), cancellationToken);

    if (claim.IsReplay && claim.CachedResponse is not null)
    {
        return _serializer.Deserialize<OrderResult>(claim.CachedResponse.Body)!;
    }

    if (!claim.IsAcquired)
    {
        throw new IdempotencyConflictException(key);
    }

    // Step 2: Open a SHARED connection and begin transaction
    await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
    await using var tx = await conn.BeginTransactionAsync(cancellationToken);

    try
    {
        // Domain operation — same connection + transaction
        var order = Order.Create(command.CustomerId, command.Amount);
        await _orderRepository.SaveAsync(order, conn, tx, cancellationToken);

        // Outbox event — same connection + transaction
        await _outbox.EnqueueAsync(new OrderCreatedEvent(order.Id), conn, tx, cancellationToken);

        var result = new OrderResult(order.Id, "Created");
        var serialized = _serializer.Serialize(result);

        // Idempotency MarkCompleted — SAME connection + transaction (atomic with domain op)
        if (_store is ITransactionalIdempotencyStore txStore)
        {
            await txStore.MarkCompletedAsync(
                tenantId, "orders", key,
                claim.OwnerToken!.Value, claim.ConcurrencyVersion!.Value,
                200, new Dictionary<string, string[]>(), serialized,
                TimeSpan.FromDays(7),
                conn, tx,  // <-- shared transaction
                cancellationToken);
        }

        // All three operations committed atomically
        await tx.CommitAsync(cancellationToken);
        return result;
    }
    catch (Exception)
    {
        await tx.RollbackAsync(cancellationToken);

        // Mark idempotency as failed AFTER rollback, using its OWN connection
        // so the failure is persisted even if the main transaction rolled back
        await _store.MarkFailedAsync(
            tenantId, "orders", key,
            claim.OwnerToken!.Value, claim.ConcurrencyVersion!.Value,
            CancellationToken.None);
        throw;
    }
}
```

> [!NOTE]
> The `MarkFailedAsync` call after rollback intentionally uses the standard `IIdempotencyStore` overload (which opens its own connection), so the failure is persisted even if the main transaction was rolled back.

---

## 4. Supported SQL Providers

| Provider | `ITransactionalIdempotencyStore` | Notes |
|---|---|---|
| `PostgreSqlIdempotencyStore` | ✅ Yes | Uses `NpgsqlConnection`/`NpgsqlTransaction` via `NpgsqlDataSource` |
| `SqlServerIdempotencyStore` | ✅ Yes | Uses `SqlConnection`/`SqlTransaction`. Guarded against `SET XACT_ABORT ON` transaction death via `UPDLOCK, HOLDLOCK` probe |
| `MySqlIdempotencyStore` | ✅ Yes | Uses `MySqlConnection`/`MySqlTransaction` via `MySqlDataSource` |
| `MariaDbIdempotencyStore` | ✅ Yes | Uses `MySqlConnection`/`MySqlTransaction` via `MySqlDataSource` |
| `SqliteIdempotencyStore` | ❌ No | Standard `IIdempotencyStore` only |
| `OracleIdempotencyStore` | ✅ Yes | Uses `OracleConnection`/`OracleTransaction` via ODP.NET Core |
| `RedisIdempotencyStore` | ❌ No | No DB transactions; Redis uses Lua atomic scripts |
| `InMemoryIdempotencyStore` | ❌ No | In-process state; no DB transactions |

### 4.1 SQL Server `SET XACT_ABORT ON` Resilience (DB-001)

In Microsoft SQL Server environments where `SET XACT_ABORT ON` is active (the default in EF Core and many enterprise database drivers), any primary key or unique index collision raises a fatal error that immediately aborts and dooms the ambient transaction (`XACT_STATE() = -1`), making rollback mandatory even when caught in a `TRY...CATCH` block.

`SqlServerIdempotencyStore` employs a defensive concurrency probe using `IF NOT EXISTS (SELECT 1 FROM idempotency_records WITH (UPDLOCK, HOLDLOCK) ...)` prior to insertion. This acquires the necessary key-range lock before inserting, preventing primary key violation errors from terminating or dooming ambient transactions.

