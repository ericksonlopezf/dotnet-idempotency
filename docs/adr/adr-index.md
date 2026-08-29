# Architecture Decision Records (ADR) Index

<!-- Copyright © Erickson Lopez. MIT License. -->

This document provides a navigable index of all Architecture Decision Records (ADRs) for the `EricksonLopez.Idempotency` ecosystem. ADRs document significant architectural choices, design constraints, and systematic rejections, providing transparent rationale for the current state of the library.

---

## ADR Categories

ADRs fall into two categories:

- **Accepted**: A decision that is in effect and implemented in the codebase.
- **Rejected / Permanently Rejected**: A design option that was evaluated and explicitly ruled out, often with an architectural boundary justification. Rejection ADRs are equally valuable — they prevent future re-evaluation of already-resolved debates.

---

## Index

| ADR | Title | Status | Date |
|---|---|---|---|
| [ADR-001](adr-001-why-idempotency-library-exists.md) | Why the EricksonLopez.Idempotency Framework Exists | Accepted | 2026-08-27 |
| [ADR-002](adr-002-idempotency-independent-of-resilience.md) | Idempotency is Independent of Resilience | Accepted | 2026-08-27 |
| [ADR-003](adr-003-idempotency-does-not-replace-concurrency.md) | Idempotency Does Not Replace Concurrency Control | Accepted | 2026-08-27 |
| [ADR-004](adr-004-lease-ownership-fencing-token-model.md) | Lease Ownership & Fencing Token Model | Accepted | 2026-08-27 |
| [ADR-005](adr-005-postgresql-dapper-persistence.md) | PostgreSQL + Dapper as Reference Persistence Strategy | Accepted | 2026-08-27 |
| [ADR-006](adr-006-deterministic-sha256-fingerprint-strategy.md) | Deterministic SHA-256 Fingerprint Strategy | Accepted | 2026-08-27 |
| [ADR-007](adr-007-transaction-coordination-model.md) | Transaction Coordination Model | Accepted | 2026-08-27 |
| [ADR-008](adr-008-outbox-integration-flow.md) | Outbox Integration Flow | Accepted | 2026-08-27 |
| [ADR-009](adr-009-mediator-pipeline-integration.md) | Mediator Pipeline Integration | Accepted | 2026-08-27 |
| [ADR-010](adr-010-native-aot-source-generators-strategy.md) | Native AOT & Source Generators Strategy | Accepted | 2026-08-27 |
| [ADR-011](adr-011-transactional-store-participation.md) | Transactional Store Participation Design | Accepted | 2026-08-27 |
| [ADR-012](adr-012-no-newtonsoft-json.md) | No Newtonsoft.Json Support | Rejected (Permanent) | 2026-08-27 |
| [ADR-013](adr-013-no-idistributedcache-abstraction.md) | No `IDistributedCache` as Core Storage Abstraction | Rejected (Permanent) | 2026-08-27 |
| [ADR-014](adr-014-no-downlevel-targeting.md) | No Downlevel Framework Targeting | Accepted (Permanent Policy) | 2026-08-27 |
| [ADR-015](adr-015-no-distributed-lock-in-core.md) | No External Distributed Lock in Core | Rejected (Permanent) | 2026-08-27 |
| [ADR-016](adr-016-no-rate-limiting-integration.md) | No Rate Limiting Integration | Rejected (Permanent) | 2026-08-27 |
| [ADR-017](adr-017-no-fusioncache-in-core.md) | No FusionCache in Core Engine | Rejected for Core / Deferred for Redis | 2026-08-27 |

---

## ADR Summaries

### ADR-001 — Why the Framework Exists
**Status**: Accepted

Establishes the founding rationale: existing solutions rely on in-memory caches, generic Redis locks without response storage, or tightly coupled ASP.NET filters without Native AOT support. `EricksonLopez.Idempotency` provides guaranteed effectively-once execution with clean architecture separation, Native AOT compatibility, and multi-database support.

---

### ADR-002 — Idempotency is Independent of Resilience
**Status**: Accepted

Idempotency (preventing duplicate side effects) and Resilience (recovering from failures) are orthogonal concerns. Resilience libraries (e.g., `EricksonLopez.Resilience`, Polly) operate at the call boundary, while idempotency operates at the state boundary. Both can compose cleanly without coupling.

---

### ADR-003 — Idempotency Does Not Replace Concurrency Control
**Status**: Accepted

Idempotency prevents duplicate execution of the same logical operation. Concurrency control (optimistic/pessimistic locking, ETag versioning) prevents stale updates on domain state. These are distinct problems. `EricksonLopez.Concurrency` handles the latter.

---

### ADR-004 — Lease Ownership & Fencing Token Model
**Status**: Accepted

Uses expiring leases with monotonically increasing `concurrency_version` fencing tokens stored atomically in the database. This prevents zombie workers from committing stale results and ensures exactly-one completion. No external distributed lock manager is required.

---

### ADR-005 — PostgreSQL + Dapper as Reference Persistence
**Status**: Accepted

PostgreSQL with Dapper and raw parameterized SQL (`ON CONFLICT DO NOTHING`) is selected as the reference storage provider for its strong atomicity guarantees, Native AOT compatibility with Npgsql 10+, and lack of ORM overhead. All other providers follow the same pattern.

---

### ADR-006 — Deterministic SHA-256 Fingerprint Strategy
**Status**: Accepted

Request fingerprints are computed as canonical SHA-256 digests over all five canonical components:
`Fingerprint = Hex(SHA-256(OperationName + ':' + Scope + ':' + TenantId + ':' + AuthenticatedSubject + ':' + PayloadBytes))`.
This prevents silent financial corruption from key reuse with altered payloads, while being deterministic and allocation-efficient via `stackalloc` spans. The resulting hexadecimal string is **uppercase**.

---

### ADR-007 — Transaction Coordination Model
**Status**: Accepted

Idempotency stores must be able to participate in the caller's database transaction to support atomic Outbox + Idempotency patterns. This is solved by `ITransactionalIdempotencyStore` (ADR-011), allowing callers to provide an `IDbConnection`/`IDbTransaction`.

---

### ADR-008 — Outbox Integration Flow
**Status**: Accepted

In the Outbox + Idempotency pattern, domain writes, outbox message insertions, and idempotency record completion must all commit in a single database transaction. `ITransactionalIdempotencyStore` enables this without ambient transactions or external coordinators.

---

### ADR-009 — Mediator Pipeline Integration
**Status**: Accepted

`IdempotencyPipelineBehavior<TRequest, TResponse>` integrates with `EricksonLopez.Mediator` as a struct-based pipeline behavior. Commands implementing `IIdempotentRequest` (exposing `IdempotencyKey` and `TenantId`) are transparently guarded before handler execution. Note: fingerprint computation serializes `TRequest`, which involves a heap allocation.

---

### ADR-010 — Native AOT & Source Generators Strategy
**Status**: Accepted

All JSON serialization uses `System.Text.Json` source generators (`IdempotencyJsonContext`) to eliminate runtime reflection. The `IsAotCompatible=true` and `EnableTrimAnalyzer=true` MSBuild properties enforce trimming safety at compile time across all packages.

---

### ADR-011 — Transactional Store Participation Design
**Status**: Accepted

Introduces `ITransactionalIdempotencyStore` as a secondary interface extending `IIdempotencyStore`, providing overloads that accept `IDbConnection?` and `IDbTransaction?`. Implemented by PostgreSQL and SQL Server providers. `InMemoryIdempotencyStore` treats these parameters as no-ops.

See the full [ADR-011 document](adr-011-transactional-store-participation.md) for the detailed option analysis and implementation record.

---

### ADR-012 — No Newtonsoft.Json Support
**Status**: Rejected (Permanent)

`Newtonsoft.Json` uses runtime reflection for contract resolution and serialization, which is fundamentally incompatible with Native AOT trimming. `System.Text.Json` with source generators is the only supported serializer. This boundary is permanent.

---

### ADR-013 — No `IDistributedCache` as Core Storage Abstraction
**Status**: Rejected (Permanent)

`IDistributedCache` provides only `Get`/`Set`/`Remove` semantics without atomic claim-and-fence primitives. Building idempotency on top of it would require external distributed locking, degrading correctness guarantees. `IIdempotencyStore` with dialect-specific atomic SQL is the correct abstraction.

---

### ADR-014 — No Downlevel Framework Targeting
**Status**: Accepted (Permanent Policy)

The library targets `net8.0;net9.0;net10.0` only. No `.NET Standard 2.0`, `.NET 6`, or `.NET 7` support. This enables clean use of C# 13 features, `Span<T>` allocations, `[LoggerMessage]`, and .NET 10 Native AOT without `#if` preprocessor pollution.

---

### ADR-015 — No External Distributed Lock in Core
**Status**: Rejected (Permanent)

Fencing tokens with database CAS operations (`MERGE WITH (HOLDLOCK)`, `ON CONFLICT`) provide provable correctness without external distributed lock managers (e.g., `RedLock`). Adding an external lock dependency would increase complexity and reduce portability.

---

### ADR-016 — No Rate Limiting Integration
**Status**: Rejected (Permanent)

Rate limiting and idempotency are distinct concerns. Rate limiting restricts the frequency of operations; idempotency ensures duplicate operations are absorbed. Combining them would violate the Single Responsibility Principle. Use `Microsoft.AspNetCore.RateLimiting` or `EricksonLopez.Resilience` for rate limiting.

---

### ADR-017 — No FusionCache in Core Engine
**Status**: Rejected for Core / Deferred for Redis Provider

`FusionCache` is a high-value caching library but introduces a dependency that constrains the core engine. The core engine remains decoupled from specific caching frameworks. Future exploration of a `FusionCache`-backed Redis adapter is deferred.

---

## Governance

ADRs are authored by the Architecture Team and reviewed before any significant change to the public API surface, storage model, or external dependency graph. All ADR decisions are dated and versioned alongside the source code.

New ADRs follow the naming convention: `adr-NNN-short-kebab-case-title.md`.
