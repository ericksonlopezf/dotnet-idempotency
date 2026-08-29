# Public API Inventory

This document provides a comprehensive inventory of public types, interfaces, value structs, and extension methods exported by the `EricksonLopez.Idempotency` package ecosystem.

---

## 1. `EricksonLopez.Idempotency.Abstractions`

### Core Contracts & Interfaces
- `IIdempotencyStore`: Primary persistence contract for claiming leases, committing completed responses, and reclaiming stale idempotency keys.
- `ITransactionalIdempotencyStore`: Extended contract supporting enlistment in active database transactions (`DbTransaction`).
- `IIdempotencyPolicy`: Contract determining lease durations, retention periods, and cacheable HTTP status codes.
- `IIdempotencyKeyProvider`: Strategy for extracting the idempotency key from incoming request contexts.
- `IIdempotencyFingerprintGenerator`: Strategy for generating canonical cryptographic request fingerprints.
- `IIdempotencySerializer`: Contract for serializing and deserializing cached HTTP/domain responses without runtime reflection.
- `IIdempotencyContextAccessor`: Accessor contract providing thread-local/async-local idempotency execution context.
- `IIdempotentRequest`: Marker interface for Mediator/CQRS commands carrying idempotency keys.

### Value Structs & Records
- `IdempotencyKey`: Readonly record struct wrapping `string Value`. Implements `IEquatable<IdempotencyKey>`, `IParsable<IdempotencyKey>`, `ISpanParsable<IdempotencyKey>`.
- `IdempotencyScope`: Readonly record struct partitioning idempotency keys across business operations.
- `IdempotencyClaimResult`: Immutable record encapsulating the result of a lease acquisition (`Status`, `OwnerToken`, `ConcurrencyVersion`, `CachedResponse`, `ConflictingFingerprint`).
- `CachedIdempotencyResponse`: Immutable record holding cached response metadata (`StatusCode`, `ContentType`, `Headers`, `BodyBytes`, `CachedAtUtc`).
- `IdempotencyContext`: Execution context containing tenant, scope, key, fingerprint, and lease tokens.
- `ClaimResultStatus`: Enum specifying claim outcomes (`AcquiredNew`, `AcquiredStale`, `CompletedReplay`, `InFlightConflict`, `FingerprintMismatch`).
- `IdempotencyStatus`: Enum indicating persistent state (`Pending`, `Completed`, `Failed`).

### Exceptions
- `IdempotencyException`: Base exception for idempotency faults.
- `IdempotencyConflictException`: Thrown when an identical operation is in-flight concurrently.
- `IdempotencyFingerprintMismatchException`: Thrown when an idempotency key is reused with a different request payload.
- `IdempotencyLeaseExpiredException`: Thrown when an operation execution exceeds the granted ownership lease.

---

## 2. `EricksonLopez.Idempotency` (Core)

### Orchestration & Serialization
- `IdempotencyEngine`: Production coordinator managing state machine transitions and execution workflows.
- `DefaultIdempotencyPolicy`: Configurable policy based on `IdempotencyOptions`.
- `IdempotencyFingerprintHasher`: Optimized static hashing engine utilizing SHA-256 with zero-allocation span buffers.
- `SystemTextJsonIdempotencySerializer`: Reflection-free serializer for response caching.
- `AsyncLocalIdempotencyContextAccessor`: AsyncLocal-backed implementation of `IIdempotencyContextAccessor`.
- `IdempotencyCleanupBackgroundService`: Periodic background worker purging expired idempotency records.

### Dependency Injection
- `ServiceCollectionExtensions`: Extensions for configuring idempotency services (`AddIdempotency()`, `AddIdempotencyStore<T>()`).

---

## 3. `EricksonLopez.Idempotency.AspNetCore`

### Middleware & ProblemDetails
- `IdempotencyMiddleware`: ASP.NET Core middleware intercepting HTTP requests containing `Idempotency-Key` headers.
- `IdempotencyProblemDetails`: Strongly typed RFC 7807 problem details model for HTTP 409 and 422 idempotency errors.
- `IdempotentAttribute`: Endpoint metadata attribute for enabling idempotency on specific Minimal API endpoints or controllers.

---

## 4. `EricksonLopez.Idempotency.Mediator`

### Pipeline Behaviors
- `IdempotencyBehavior<TRequest, TResponse>`: Pipeline behavior for `EricksonLopez.Mediator` enforcing effectively-once execution on `IIdempotentRequest` commands.

---

## 5. `EricksonLopez.Idempotency.Result`

### Functional Extensions
- `IdempotencyResultExtensions`: Extensions bridging idempotency execution to `Result<T>`.
- `IdempotencyErrors`: Factory methods producing structured `Error.Conflict` and `Error.Validation` descriptors.

---

## 6. Storage Provider Packages

- `EricksonLopez.Idempotency.Redis`: Distributed storage using StackExchange.Redis with atomic Lua scripts.
- `EricksonLopez.Idempotency.PostgreSql`: PostgreSQL storage using Dapper with `FOR UPDATE SKIP LOCKED`.
- `EricksonLopez.Idempotency.SqlServer`: SQL Server storage using `sp_getapplock` and rowversioning.
- `EricksonLopez.Idempotency.MySql`: MySQL storage with row-level locks.
- `EricksonLopez.Idempotency.MariaDb`: MariaDB storage with transactional locking.
- `EricksonLopez.Idempotency.Oracle`: Oracle Database storage using `FOR UPDATE NOWAIT`.
- `EricksonLopez.Idempotency.Sqlite`: SQLite storage for single-node embedded persistence.
- `EricksonLopez.Idempotency.Testing`: In-memory thread-safe storage for unit and integration testing.
