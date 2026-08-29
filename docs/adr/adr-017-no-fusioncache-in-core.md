# ADR-017: No FusionCache in Core; Redis Package Scope Only

**Status**: Rejected (for Core) / Deferred (for Redis package)  
**Date**: 2026-08-27  
**Author**: Erickson López  
**Deciders**: Architecture Team  
**Tags**: fusioncache, caching, redis, dependencies, storage-layer

---

## Context

`ZiggyCreatures.FusionCache` is a popular .NET caching library that provides:
- Multi-level (L1 memory + L2 distributed) caching
- Automatic fail-safe and factory calls
- Stampede protection
- OpenTelemetry integration
- Pluggable backends (Redis, SQL Server, etc.)

Contributors have suggested using `FusionCache` as the storage backend for `EricksonLopez.Idempotency`
to benefit from its multi-level caching, automatic fail-safe, and stampede protection.

---

## Decision

**REJECTED for core. DEFERRED for the future `EricksonLopez.Idempotency.Redis` package.**

Specifically:
- `FusionCache` will NOT be added as a dependency to `EricksonLopez.Idempotency` (core).
- `FusionCache` will NOT be added to any SQL provider package.
- The possibility of using `FusionCache` inside the optional `EricksonLopez.Idempotency.Redis`
  package is left open for evaluation when that package is built (v1.2.0).

---

## Reasoning

### 1. FusionCache has cache semantics, not store semantics

The same reasoning that applies to `IDistributedCache` (ADR-013) applies to `FusionCache`:

`FusionCache` is designed for **cached data** where stale reads are acceptable and the data can be
recreated from a source of truth. Idempotency records are not cached data — they are **authoritative
records of execution state**:

- A "stale read" from the cache that says "not found" when a record exists would cause double execution.
- `FusionCache`'s fail-safe behavior (serving stale data when the backend is unavailable) is
  incompatible with idempotency semantics (where "not found" must be authoritative).
- The atomic acquire-and-lock operation (`TryAcquireAsync`) cannot be expressed through `FusionCache`'s
  `GetOrSetAsync` pattern.

### 2. FusionCache is not needed for SQL providers — SQL is the source of truth

For the 6 SQL providers (PostgreSQL, SQL Server, MySQL, MariaDB, Oracle, SQLite), the database itself
provides all the consistency guarantees needed:
- Atomic INSERT with conflict detection (`ON CONFLICT DO NOTHING`)
- Conditional UPDATE with version check (fencing tokens)
- Transactional participation (planned in ADR-011)

Adding `FusionCache` as an intermediate layer between the application and the database would introduce:
- Cache invalidation complexity (when to evict, how to handle race conditions)
- Additional memory overhead for an in-memory L1 layer that doesn't apply to idempotency reads
- False cache hits if an in-flight record is cached as "not found"

### 3. AOT compatibility of FusionCache is not fully validated

`ZiggyCreatures.FusionCache` does not explicitly declare `IsAotCompatible=true` as of the evaluation
date. Adding it as a dependency would require validating AOT compatibility and may introduce trimming
warnings that would break the build (due to `TreatWarningsAsErrors=true`).

### 4. FusionCache in the Redis provider — deferred evaluation

The planned `EricksonLopez.Idempotency.Redis` package may benefit from `FusionCache`'s multi-level
caching (L1 memory + L2 Redis) for read-heavy idempotency scenarios:

- Completed records can be cached in L1 memory for fast replay without hitting Redis
- `FusionCache`'s stampede protection aligns with the in-flight conflict detection requirement

However, this evaluation must happen:
1. After `EricksonLopez.Idempotency.Redis` v1.0.0 is available using direct StackExchange.Redis
2. After `FusionCache` AOT compatibility is confirmed or their team publishes a roadmap
3. After performance benchmarks show that a multi-level cache provides meaningful improvement
   over direct Redis calls for the idempotency read pattern

---

## Consequences

### For the core and SQL providers (current behavior)

No change. `FusionCache` is not a dependency. SQL providers continue to use direct database connections.

### For future Redis provider evaluation

When building `EricksonLopez.Idempotency.Redis`:
1. Start with `StackExchange.Redis` + Lua scripts (direct, no FusionCache).
2. Measure read-through performance in completed-record replay scenarios.
3. If L1 caching would provide meaningful benefit AND `FusionCache` is AOT-compatible, add it as
   an OPTIONAL dependency in the Redis package with an opt-in configuration.

### For contributors opening PRs that add FusionCache to core or SQL providers

Pull requests that add `FusionCache` as a dependency to the core or any SQL provider package will
be **rejected with reference to this ADR** until the Redis provider evaluation phase.

---

## Alternatives Considered

| Alternative | Verdict |
|---|---|
| `FusionCache` as core storage abstraction | REJECTED — cache semantics incompatible with store semantics |
| `FusionCache` in SQL providers | REJECTED — SQL is the source of truth; no caching layer needed |
| `FusionCache` in Redis provider | DEFERRED — evaluate after Redis provider v1.0.0 is built |
| `IMemoryCache` as read-through cache for completed records | CONSIDERED — may be explored as a lightweight alternative if needed |

---

## References

- ADR-012: No Newtonsoft.Json Support
- ADR-013: No IDistributedCache as Core Storage Abstraction
- ADR-015: No External Distributed Lock in Core
- [ZiggyCreatures.FusionCache — GitHub](https://github.com/ZiggyCreatures/FusionCache)
- [FusionCache and Native AOT — tracked issue](https://github.com/ZiggyCreatures/FusionCache/issues)
