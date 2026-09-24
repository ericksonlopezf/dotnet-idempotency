# Master Feature & Storage Provider Matrix

This document provides a comprehensive technical reference for idempotency guarantees, storage provider capabilities, distributed locking algorithms, and Native AOT compatibility across the `EricksonLopez.Idempotency` ecosystem.

---

## 1. Storage Provider Capabilities Matrix

| Storage Provider | Package | Lease Expiration Mechanism | Distributed Lock / Atomic Primitive | Multi-Tenancy Partitioning | Atomic CAS / Version Check | Native AOT Compatible |
| :--- | :--- | :--- | :--- | :--- | :--- | :---: |
| **In-Memory** | `EricksonLopez.Idempotency.Testing` | `TimeProvider` TTL | `ConcurrentDictionary` CAS | Composite Key | Monotonic Version | Yes |
| **Redis** | `EricksonLopez.Idempotency.Redis` | Native Redis Key TTL (PX) | Atomic Lua script (`EVAL`) | Key Prefix (`{tenant}:{scope}:{key}`) | Lua CAS Evaluation | Yes |
| **PostgreSQL** | `EricksonLopez.Idempotency.PostgreSql` | `lease_expires_at_utc` Column | `ON CONFLICT DO NOTHING` | Column `tenant_id` | `UPDATE ... RETURNING concurrency_version` | Yes |
| **SQL Server** | `EricksonLopez.Idempotency.SqlServer` | `lease_expires_at_utc` Column | `IF NOT EXISTS (UPDLOCK, HOLDLOCK) INSERT` | Column `tenant_id` | `UPDATE ... OUTPUT INSERTED.concurrency_version` | Yes |
| **MySQL** | `EricksonLopez.Idempotency.MySql` | `lease_expires_at_utc` Column | `INSERT IGNORE INTO` | Column `tenant_id` | Version Conditioned `UPDATE` | Yes |
| **MariaDB** | `EricksonLopez.Idempotency.MariaDb` | `lease_expires_at_utc` Column | `INSERT IGNORE INTO` | Column `tenant_id` | Version Conditioned `UPDATE` | Yes |
| **Oracle** | `EricksonLopez.Idempotency.Oracle` | `lease_expires_at_utc` Column | `MERGE INTO ... USING DUAL` | Column `tenant_id` | `UPDATE ... RETURNING concurrency_version INTO` | **No** (Driver reflection) |
| **SQLite** | `EricksonLopez.Idempotency.Sqlite` | `lease_expires_at_utc` Column | `INSERT OR IGNORE INTO` | Column `tenant_id` | Version Conditioned `UPDATE` | Yes |

---

## 2. Claim State Machine & Conflict Resolution Matrix

| Outcome (`ClaimResultStatus`) | HTTP Code | Middleware Action | Client Response | Distributed Safety Invariant |
| :--- | :---: | :--- | :--- | :--- |
| **`AcquiredNew`** | N/A | Execute Operation | Live Response from Handler | Caller holds exclusive ownership lease with `OwnerToken`. |
| **`AcquiredStale`** | N/A | Execute Operation (Reclaim) | Live Response from Handler | Previous worker crashed/expired; reclaimed with new `OwnerToken`. |
| **`CompletedReplay`** | Cached | Replay Cached Response | Stored Response + `X-Idempotency-Replayed: true` | Payload deserialized from store; operation not executed. |
| **`InFlightConflict`** | 409 Conflict | Intercept & Return 409 | RFC 9110 `IdempotencyProblemDetails` | Concurrent request with same key in progress; prevents double mutation. |
| **`FingerprintMismatch`** | 409 Conflict | Intercept & Return 409 | RFC 9110 `IdempotencyProblemDetails` | Key reused with different payload/headers; rejected immediately. |

---

## 3. Fingerprint Generator Matrix

| Generator / Algorithm | Canonical Elements Included | Hashing Algorithm | Output Representation | Zero-Allocation Optimizations |
| :--- | :--- | :--- | :--- | :--- |
| **Standard Request Fingerprint** | HTTP Method, Request Path, Scope, Tenant ID, Authenticated Subject, Payload Bytes | SHA-256 | 64-character uppercase Hex string | Stackalloc span buffers, minimal heap allocations on short paths |
| **Custom Fingerprint SPI** | Application-defined components via `IIdempotencyFingerprintGenerator` | SHA-256 / Custom | Hex / Base64 string | Fully pluggable through DI registration |

---

## 4. Framework Integration Matrix

| Integration | Package | Key Mechanism | Telemetry & Observability |
| :--- | :--- | :--- | :--- |
| **ASP.NET Core** | `EricksonLopez.Idempotency.AspNetCore` | `IdempotentEndpointFilter`, `IdempotencyMiddleware`, `[Idempotent]` | Activity spans, `idempotency.requests`, `idempotency.replayed`, Problem Details RFC 9110 |
| **EricksonLopez.Mediator** | `EricksonLopez.Idempotency.Mediator` | `IdempotencyPipelineBehavior<TRequest, TResponse>` | CQRS pipeline execution with automatic key extraction from `IIdempotentRequest` |
| **EricksonLopez.Result** | `EricksonLopez.Idempotency.Result` | `IdempotencyErrors`, `AsErrorResult<T>()` | Translates conflicts into typed `Error.Conflict` descriptors |
