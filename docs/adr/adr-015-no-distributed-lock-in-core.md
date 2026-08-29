# ADR-015: No External Distributed Lock in Core

**Status**: Rejected (Permanent)  
**Date**: 2026-08-27  
**Author**: Erickson López  
**Deciders**: Architecture Team  
**Tags**: distributed-lock, redis, redlock, correctness, fencing-tokens

---

## Context

Several community members have suggested adding an external distributed lock library (e.g.,
`RedLock.net`, `StackExchange.Redis` lock primitives, or `Medallion.Threading`) as a concurrency
mechanism in `EricksonLopez.Idempotency` to:

1. Prevent concurrent execution of the same idempotency key across multiple application instances.
2. Provide an alternative to the current SQL-based lease ownership model.
3. Enable Redis-based idempotency without a custom `IIdempotencyStore` implementation.

---

## Decision

**REJECTED. No external distributed lock library will be added to the core of `EricksonLopez.Idempotency`.**

The library will continue to rely exclusively on the **lease ownership + fencing token** model
described in ADR-004.

---

## Reasoning

### 1. The current model already prevents concurrent execution — without external locks

`EricksonLopez.Idempotency` uses database-native atomic operations to prevent concurrent execution:

**PostgreSQL** — `ON CONFLICT DO NOTHING RETURNING`:
```sql
INSERT INTO idempotency_records (tenant_id, scope, key, owner_token, concurrency_version, status, ...)
VALUES (@tenantId, @scope, @key, @ownerToken, 1, 'InFlight', ...)
ON CONFLICT (tenant_id, scope, key) DO NOTHING
RETURNING *;
```

If two requests arrive simultaneously with the same key:
- First insert succeeds → `TryAcquire` returns `LeaseAcquired`
- Second insert: `ON CONFLICT DO NOTHING` → `TryAcquire` returns `InFlightConflict` → HTTP 409

This is a **database-enforced, atomic mutual exclusion** at the storage layer. No external lock is needed.

### 2. Distributed locks have documented correctness failures under process pause / GC pressure

The fundamental problem with distributed locks for critical section protection is described in detail by
Martin Kleppmann in "Designing Data-Intensive Applications" (Chapter 9) and in the blog post
"How to do distributed locking" (2016):

> "Even if you implement the lock perfectly correctly, the whole approach to mutual exclusion is fundamentally
>  broken for ensuring file access: if a process in a critical section happens to be paused (due to GC,
>  VM scheduler, etc.) for longer than the lock TTL, the lock expires, another process acquires the lock,
>  and the original process wakes up thinking it still holds the lock."

This scenario — known as the **zombie worker problem** — is specifically addressed by `EricksonLopez.Idempotency`'s
fencing tokens:

```sql
-- MarkCompletedAsync: only succeeds if concurrency_version matches (fencing check)
UPDATE idempotency_records
SET status = 'Completed', response_body = @body, concurrency_version = @expected + 1
WHERE tenant_id = @tenantId AND key = @key AND concurrency_version = @expected
```

A zombie worker cannot overwrite results from a more recent worker because the `concurrency_version`
will not match. With distributed locks, there is no equivalent protection after the lock TTL expires.

### 3. External distributed locks introduce cross-dependency complexity

Adding a distributed lock library creates the following problems:

- **New required infrastructure**: A Redis instance or other lock backend is required even for SQL-backed
  stores where SQL itself can provide the concurrency guarantee.
- **Dependency coupling**: `RedLock.net` or `Medallion.Threading` becomes a mandatory dependency,
  increasing package footprint and introducing their own NuGet transitive dependencies.
- **AOT compatibility risk**: Lock libraries typically use async state machine features and timers that
  need to be validated against Native AOT.
- **Dual concurrency models**: Having both SQL atomic inserts AND external locks is redundant and creates
  confusion about which mechanism is authoritative.

### 4. The fencing token model is provably correct for exactly-once guarantees

The current model provides a mathematical guarantee:

```
Let f(n) = concurrency_version of record at time n
For any worker W trying to complete at time t:
  UPDATE ... WHERE concurrency_version = f(t₀)
  If f(t) != f(t₀) → UPDATE affects 0 rows → no mutation
  If f(t) = f(t₀) → UPDATE succeeds → concurrency_version increments
```

Since `concurrency_version` is strictly monotonically increasing, it is impossible for an older
write to overwrite a newer one. This is the property that distributed locks CANNOT provide when
a lock TTL expires while a worker is in the middle of an operation.

---

## Consequences

### For teams using SQL stores

No change required. SQL-native atomic operations already provide the necessary concurrency guarantee.

### For teams wanting Redis support

The planned `EricksonLopez.Idempotency.Redis` package (v1.2.0) will implement concurrency using
**Lua scripts for atomic compare-and-swap** operations in Redis — which provides comparable (though
slightly weaker) correctness guarantees without external distributed lock libraries:

```lua
-- Atomic SETNX equivalent with version check
if redis.call('GET', KEYS[1]) == false then
  redis.call('HSET', KEYS[1], 'owner', ARGV[1], 'version', 1, 'status', 'InFlight')
  return 1  -- acquired
else
  return 0  -- conflict
end
```

---

## Alternatives Considered

| Alternative | Verdict |
|---|---|
| `RedLock.net` as distributed lock for concurrency | REJECTED — redundant with SQL atomic operations; zombie worker risk |
| `Medallion.Threading` distributed locks | REJECTED — same reasons as RedLock.net |
| Redis `SET NX PX` as lock primitive | REJECTED — superseded by Lua atomic scripts in planned Redis provider |
| Database `SERIALIZABLE` transaction isolation | REJECTED — too expensive; deadlock-prone at scale |
| Current SQL atomic INSERT (ON CONFLICT) + fencing tokens | ACCEPTED — current implementation |

---

## References

- ADR-004: Lease Ownership & Fencing Token Model
- ADR-013: No IDistributedCache as Core Storage Abstraction
- [Kleppmann — How to do distributed locking (2016)](https://martin.kleppmann.com/2016/02/08/how-to-do-distributed-locking.html)
- [Kleppmann, "Designing Data-Intensive Applications", Chapter 9]
- `PostgreSqlIdempotencyStore.cs` — ON CONFLICT atomic acquire
- `IIdempotencyStore.MarkCompletedAsync` — fencing token conditional update
