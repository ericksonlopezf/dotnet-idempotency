# ADR-013: No IDistributedCache as Core Storage Abstraction

**Status**: Rejected (Permanent)  
**Date**: 2026-08-27  
**Author**: Erickson López  
**Deciders**: Architecture Team  
**Tags**: storage, abstraction, idistributedcache, correctness, exactly-once

---

## Context

`Microsoft.Extensions.Caching.Distributed.IDistributedCache` is the standard .NET abstraction for
distributed caching. It is used by `IdempotentAPI` (the primary competitor) as the backing storage
interface. Several contributors and evaluators have requested that `EricksonLopez.Idempotency` adopt
`IDistributedCache` as its storage interface to:

1. Leverage existing infrastructure (Redis, SQL Server output cache, etc.) without custom setup.
2. Be compatible with any `IDistributedCache` provider without writing custom `IIdempotencyStore` implementations.
3. Simplify the API surface.

---

## Decision

**REJECTED. `IDistributedCache` will never become the primary storage interface for `EricksonLopez.Idempotency`.**

The library will continue to use the purpose-built `IIdempotencyStore` SPI.

---

## Reasoning

### 1. IDistributedCache has cache semantics, not store semantics

`IDistributedCache` defines three operations:

```csharp
Task<byte[]?> GetAsync(string key, CancellationToken token);
Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token);
Task RemoveAsync(string key, CancellationToken token);
```

These operations are sufficient for a **cache** but fundamentally insufficient for an **idempotency store**. The key differences:

| Requirement | IDistributedCache | IIdempotencyStore |
|---|---|---|
| Atomic acquire-or-conflict detection | NOT SUPPORTED | `TryAcquireAsync` (atomic SQL/MERGE) |
| Lease ownership (who is processing) | NOT SUPPORTED | `OwnerToken: Guid` |
| Fencing tokens (zombie-proof version) | NOT SUPPORTED | `ConcurrencyVersion: int` |
| Mark as completed (with fencing check) | NOT SUPPORTED | `MarkCompletedAsync` (conditional SQL) |
| Mark as failed (release for retry) | NOT SUPPORTED | `MarkFailedAsync` |
| In-flight conflict detection (409) | NOT SUPPORTED | `ClaimResultStatus.InFlightConflict` |
| Fingerprint mismatch detection | NOT SUPPORTED | `ClaimResultStatus.FingerprintMismatch` |
| Expiration cleanup (batch purge) | TTL-based eviction | `CleanupExpiredRecordsAsync` |

Using `IDistributedCache`, the only way to implement idempotency is:
1. `Get(key)` — check if already cached.
2. Use a distributed lock (`RedLock`, `SemaphoreSlim`, etc.) to prevent concurrent execution.
3. `Set(key, response, options)` — cache the result.

This approach has critical correctness problems (see point 2).

### 2. IDistributedCache cannot guarantee exactly-once — it guarantees at-most-once

The `IDistributedCache + distributed lock` pattern suffers from **zombie worker** scenarios:

```
Worker A: GET key → null → acquire RedLock → start processing
RedLock TTL expires before Worker A finishes
Worker B: GET key → null → acquire RedLock → start processing (concurrently with Worker A!)
Worker A: SET key → stores response-A
Worker B: SET key → stores response-B (overwrites response-A!)
Client: GET key → receives response-B (the WRONG response for idempotency)
```

This is a documented failure mode of distributed locks. Martin Kleppmann explains this in
"Designing Data-Intensive Applications", Chapter 9 ("The trouble with distributed locks"):
> "A distributed lock provides no safety guarantees against process pauses or packet losses."

`IIdempotencyStore` with fencing tokens prevents this:

```sql
-- TryAcquireAsync: atomic INSERT (exactly one worker succeeds)
INSERT INTO idempotency_records (tenant_id, scope, key, ...)
ON CONFLICT (tenant_id, scope, key) DO NOTHING
RETURNING ...

-- MarkCompletedAsync: conditional UPDATE with fencing token
UPDATE idempotency_records
SET status = 'Completed', response_body = @body, concurrency_version = @expected + 1
WHERE tenant_id = @tenantId AND key = @key AND concurrency_version = @expected
-- If Worker A (zombie) tries to write after Worker B completed:
-- concurrency_version != @expected → UPDATE affects 0 rows → no data corruption
```

### 3. Multi-tenancy requires a schema that IDistributedCache cannot provide

`IIdempotencyStore` uses a composite key `(TenantId, Scope, Key)` that provides per-tenant isolation
at the **storage layer** — not at the application layer. This means:

- Two tenants CAN use the same idempotency key string without collision.
- Tenant isolation is an invariant of the database schema (`PRIMARY KEY (tenant_id, scope, key)`).

With `IDistributedCache`, the only way to achieve per-tenant isolation is to prefix keys:
```csharp
var cacheKey = $"{tenantId}:{scope}:{key}";
```

This is an application-layer convention, not a storage-layer invariant. It can be bypassed by bugs,
and it does not prevent cross-tenant queries if a consumer bypasses the key-building convention.

### 4. IIdempotencyStore is a small, focused interface

The concern about API surface complexity is valid but overstated. `IIdempotencyStore` has 4 methods:
- `TryAcquireAsync` — atomic acquire
- `MarkCompletedAsync` — mark as done
- `MarkFailedAsync` — mark as failed (for retry)
- `CleanupExpiredRecordsAsync` — housekeeping

These 4 methods express the complete state machine of idempotency. `IDistributedCache`'s 3 methods
(`Get`, `Set`, `Remove`) cannot express this state machine without additional infrastructure.

### 5. IDistributedCache as a backing implementation is a separate concern

This rejection is specifically about `IDistributedCache` as the **core abstraction** of `IIdempotencyStore`.

A future `EricksonLopez.Idempotency.Redis` package WILL implement `IIdempotencyStore` over Redis
directly (using Lua scripts for atomic operations) — WITHOUT using `IDistributedCache`. This provides
Redis support while maintaining the correctness guarantees of the `IIdempotencyStore` contract.

---

## Consequences

### What happens if a consumer wants Redis support

Wait for `EricksonLopez.Idempotency.Redis` (planned for v1.2.0), which will implement `IIdempotencyStore`
using `StackExchange.Redis` + Lua scripts for atomic operations.

### What happens to contributors who open PRs replacing IIdempotencyStore with IDistributedCache

Pull requests that replace the `IIdempotencyStore` contract with `IDistributedCache` will be
**rejected with reference to this ADR**.

---

## Alternatives Considered

| Alternative | Verdict |
|---|---|
| Replace `IIdempotencyStore` with `IDistributedCache` | REJECTED — degrades correctness guarantees |
| Add `IDistributedCache`-based adapter in the core | REJECTED — confuses consumers about correctness guarantees |
| `EricksonLopez.Idempotency.DistributedCache` optional package | REJECTED — would communicate weaker guarantees than what the library promises |
| `EricksonLopez.Idempotency.Redis` using `IConnectionMultiplexer` + Lua scripts | ACCEPTED — planned for v1.2.0 |

---

## References

- ADR-004: Lease Ownership & Fencing Token Model
- [Kleppmann, "Designing Data-Intensive Applications", Chapter 9]
- [How to do distributed locking — Martin Kleppmann](https://martin.kleppmann.com/2016/02/08/how-to-do-distributed-locking.html)
- `IIdempotencyStore.cs` — current 4-method SPI
- `PostgreSqlIdempotencyStore.cs` — ON CONFLICT atomic acquire pattern
