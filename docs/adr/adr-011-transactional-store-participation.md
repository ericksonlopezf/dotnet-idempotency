# ADR-011: Transactional Store Participation Design

**Status**: Accepted  
**Date**: 2026-08-27  
**Author**: Erickson López  
**Deciders**: Architecture Team  
**Tags**: transaction, outbox, consistency, store, IDbConnection

---

## Context

`IIdempotencyStore` implementors (e.g., `PostgreSqlIdempotencyStore`, `SqlServerIdempotencyStore`) currently
open their own internal database connections. This violates the atomicity requirement documented in ADR-007
("Transaction Coordination Model"), which promises that idempotency records can participate in the same
database transaction as the domain operation.

### The Problem

In the **Outbox + Idempotency** pattern, the following sequence must be atomic:

```
BEGIN TRANSACTION
  1. Execute domain command (e.g., create payment record)
  2. Insert Outbox message (for async event publication)
  3. MarkCompletedAsync (record idempotency state)
COMMIT TRANSACTION
```

If step 3 creates its own connection, the atomicity guarantee breaks:
- Steps 1-2 can succeed (transaction committed)
- Step 3 can fail (its own independent connection)
- Result: domain operation succeeded + outbox enqueued, but idempotency record not persisted
- On retry: the system re-executes the domain command (violating exactly-once semantics)

This is a semantic correctness bug, not just a performance concern.

### Current State

The bug exists in:
- `PostgreSqlIdempotencyStore` — uses `NpgsqlConnection` opened internally
- `SqlServerIdempotencyStore` — uses `SqlConnection` opened internally
- All other SQL providers follow the same pattern

`InMemoryIdempotencyStore` (testing) is not affected since it uses in-process state.

---

## Decision

### Option A: Ambient Transaction (TransactionScope) — REJECTED

Use `System.Transactions.TransactionScope` to allow stores to automatically enlist in an ambient transaction.

**Why rejected**:
- `TransactionScope` is not available in NativeAOT scenarios (it uses reflection internally for distributed transaction promotion in some pathways).
- Requires the caller to manage `TransactionScope` lifetime, creating implicit coupling.
- Async ambient transactions have well-known issues with `async`/`await` (flow must be explicitly managed with `TransactionScopeAsyncFlowOption.Enabled`).
- Not idiomatic with modern .NET async code.

### Option B: ITransactionalIdempotencyStore secondary interface — ACCEPTED

Add a secondary interface `ITransactionalIdempotencyStore : IIdempotencyStore` that defines the transactional
overloads for `MarkCompletedAsync` and `MarkFailedAsync`, accepting a caller-provided `IDbConnection` (required,
non-nullable) and optional `IDbTransaction?`:

```csharp
// Transactional MarkCompletedAsync overload — requires an existing open connection
Task<bool> MarkCompletedAsync(
    Guid tenantId,
    string scope,
    IdempotencyKey key,
    Guid ownerToken,
    int concurrencyVersion,
    int statusCode,
    IReadOnlyDictionary<string, string[]> headers,
    ReadOnlyMemory<byte> responseBody,
    TimeSpan retentionDuration,
    IDbConnection connection,        // ← NON-NULLABLE, required — caller must provide an open connection
    IDbTransaction? transaction,     // ← NULLABLE — optional; operation runs in the provided transaction if non-null
    CancellationToken cancellationToken = default);

// Transactional MarkFailedAsync overload — same pattern
Task<bool> MarkFailedAsync(
    Guid tenantId,
    string scope,
    IdempotencyKey key,
    Guid ownerToken,
    int concurrencyVersion,
    IDbConnection connection,        // ← NON-NULLABLE, required
    IDbTransaction? transaction,     // ← NULLABLE — optional
    CancellationToken cancellationToken = default);
```

> **Note:** The initial design considered a unified overload with `IDbConnection? connection = null` (nullable with
> default). The final implementation separates this into **two distinct overloads** per the base `IIdempotencyStore`
> contract (without connection) and the `ITransactionalIdempotencyStore` contract (with required connection).
> The `connection` parameter is **non-nullable and mandatory** — callers must provide an open connection, as the
> store will not open a new one.

Implementation strategy:
- If `transaction` is not `null`, the store uses the provided connection and transaction without opening a new one.
- If `transaction` is `null`, the store uses the provided connection but without an explicit transaction.

**Why accepted**:
- No breaking change to existing `IIdempotencyStore` implementations (new overload added).
- Explicit is better than implicit (caller controls the transaction lifecycle).
- Compatible with NativeAOT (no ambient transactions, no reflection).
- Works with all supported DB providers (Npgsql, SqlClient, MySqlConnector, etc.) via `IDbConnection` abstraction.
- Consistent with patterns used by Dapper and other micro-ORM libraries.

### Option C: ITransactionParticipant interface — CONSIDERED (superseded by Option B)

Add a secondary interface `ITransactionalIdempotencyStore : IIdempotencyStore` for stores that support transactional participation.

**Why superseded**: Option B (as implemented) IS this approach — the interface was named `ITransactionalIdempotencyStore`, matching this option. The implementation is cleaner than modifying `IIdempotencyStore` directly because:
- Existing `IIdempotencyStore` implementations do NOT need changes.
- Consumers check `store is ITransactionalIdempotencyStore txStore` to opt into transactional behavior.
- Less API surface on the base contract.

---

## Consequences

### Positive
- Enables the Outbox + Idempotency pattern with true atomicity.
- No breaking changes to existing store implementations (additive overload).
- Explicit API surface — consumers understand exactly what they're opting into.
- Native AOT compatible.

### Negative
- Consumers who need transactional participation must manage `IDbConnection`/`IDbTransaction` lifetime.
- The base `IIdempotencyStore` interface grows slightly in complexity.
- Implementing stores must provide a second implementation path (with and without external connection).

### Neutral
- `InMemoryIdempotencyStore` can implement the overloads as no-ops (ignoring the connection/transaction parameters), since in-memory stores don't have DB transactions.

---

## Implementation

**Status: Implemented in v1.0.0**

1. ✅ Created `ITransactionalIdempotencyStore` interface in `EricksonLopez.Idempotency.Abstractions`.
2. ✅ `PostgreSqlIdempotencyStore` implements `ITransactionalIdempotencyStore` — transactional overloads use caller-provided `IDbConnection`/`IDbTransaction`.
3. ✅ `SqlServerIdempotencyStore` implements `ITransactionalIdempotencyStore` — same pattern.
4. ✅ `InMemoryIdempotencyStore` (Testing) implements `IIdempotencyStore` only — no DB transactions in-memory.
5. ✅ `docs/transaction-integration.md` contains working code example.

---

## References

- ADR-004: Lease Ownership & Fencing Token Model
- ADR-007: Transaction Coordination Model
- ADR-008: Outbox Integration Flow
- [Kleppmann, "Designing Data-Intensive Applications", Chapter 9: Consistency and Consensus]
- [PostgreSQL — BEGIN/COMMIT semantics with Npgsql](https://www.npgsql.org/doc/transactions.html)
