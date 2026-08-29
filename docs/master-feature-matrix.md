# Master Feature & Storage Provider Matrix

This document provides a comprehensive technical reference for idempotency guarantees, storage provider capabilities, distributed locking algorithms, and Native AOT compatibility across the `EricksonLopez.Idempotency` ecosystem.

---

## 1. Storage Provider Capabilities Matrix

| Storage Provider | Package | Lease Expiration Mechanism | Distributed Lock Primitive | Multi-Tenancy Partitioning | Atomic CAS / Version Check | Native AOT Compatible |
| :--- | :--- | :--- | :--- | :--- | :--- | :---: |
| **In-Memory** | `EricksonLopez.Idempotency.Testing` | `TimeProvider` TTL | `ConcurrentDictionary` CAS | Composite Key | Monotonic Version | Yes |
| **Redis** | `EricksonLopez.Idempotency.Redis` | Native Redis Key TTL (PX) | `SET key token NX PX ttl` (Lua CAS) | Key Prefix (`{tenant}:{scope}:{key}`) | Lua Script Evaluation | Yes |
| **PostgreSQL** | `EricksonLopez.Idempotency.PostgreSql` | `lease_expires_at` Column | `FOR UPDATE SKIP LOCKED` / Advisory | Column `tenant_id` | Version Conditioned UPDATE | Yes |
| **SQL Server** | `EricksonLopez.Idempotency.SqlServer` | `LeaseExpiresAt` Column | `sp_getapplock` / `UPDLOCK` | Column `TenantId` | Version Conditioned UPDATE | Yes |
| **MySQL** | `EricksonLopez.Idempotency.MySql` | `lease_expires_at` Column | `FOR UPDATE NOWAIT` | Column `tenant_id` | Version Conditioned UPDATE | Yes |
| **MariaDB** | `EricksonLopez.Idempotency.MariaDb` | `lease_expires_at` Column | `FOR UPDATE NOWAIT` | Column `tenant_id` | Version Conditioned UPDATE | Yes |
| **Oracle** | `EricksonLopez.Idempotency.Oracle` | `LEASE_EXPIRES_AT` Column | `FOR UPDATE NOWAIT` / `DBMS_LOCK` | Column `TENANT_ID` | Version Conditioned UPDATE | Yes |
| **SQLite** | `EricksonLopez.Idempotency.Sqlite` | `lease_expires_at` Column | Database Exclusive Lock | Column `tenant_id` | Single-Writer Transaction | Yes |

---

## 2. Claim State Machine & Conflict Resolution Matrix

| Outcome (`ClaimResultStatus`) | HTTP Code | Middleware Action | Client Response | Distributed Safety Invariant |
| :--- | :---: | :--- | :--- | :--- |
| **`AcquiredNew`** | N/A | Execute Operation | Live Response from Handler | Caller holds exclusive ownership lease with `OwnerToken`. |
| **`AcquiredStale`** | N/A | Execute Operation (Reclaim) | Live Response from Handler | Previous worker crashed/expired; reclaimed with new `OwnerToken`. |
| **`CompletedReplay`** | Cached | Replay Cached Response | Stored Response + `Idempotent-Replay: true` | Payload deserialized from store; operation not executed. |
| **`InFlightConflict`** | 409 Conflict | Intercept & Return 409 | RFC 7807 `IdempotencyProblemDetails` | Concurrent request with same key in progress; prevents double mutation. |
| **`FingerprintMismatch`** | 422 Unprocessable | Intercept & Return 422 | RFC 7807 `IdempotencyProblemDetails` | Key reused with different payload/headers; rejected immediately. |

---

## 3. Fingerprint Generator Matrix

| Generator / Algorithm | Canonical Elements Included | Hashing Algorithm | Output Representation | Zero-Allocation Optimizations |
| :--- | :--- | :--- | :--- | :--- |
| **Standard Request Fingerprint** | HTTP Method, Request Path, Query String, Tenant ID, Authenticated Subject, Normalized Body Bytes | SHA-256 | 64-character lowercase Hex string | Stackalloc span buffers, zero intermediate string heap allocations |
| **Custom Header Fingerprint** | Configured whitelist of RFC/Custom Headers | SHA-256 / FarmHash | Hex string | Lexicographical header sorting before digest computation |

---

## 4. Framework Integration Matrix

| Integration | Package | Key Mechanism | Telemetry & Observability |
| :--- | :--- | :--- | :--- |
| **ASP.NET Core** | `EricksonLopez.Idempotency.AspNetCore` | `IdempotencyMiddleware`, `[Idempotent]` endpoint filter | Activity spans, `idempotency.replays` metric, `Idempotency-Key` header |
| **EricksonLopez.Mediator** | `EricksonLopez.Idempotency.Mediator` | `IdempotencyBehavior<TRequest, TResponse>` | CQRS pipeline execution with automatic key extraction from `IIdempotentRequest` |
| **EricksonLopez.Result** | `EricksonLopez.Idempotency.Result` | `IdempotencyErrors`, `Result<T>` caching | Translates conflicts into typed `Error.Conflict` and `Error.Validation` |
