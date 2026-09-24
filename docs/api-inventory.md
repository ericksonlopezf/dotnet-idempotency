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

### Orchestration, Diagnostics & Serialization
- `IdempotencyEngine`: Production coordinator managing state machine transitions and execution workflows.
- `DefaultIdempotencyPolicy`: Configurable policy based on `IdempotencyOptions`.
- `IdempotencyFingerprintHasher`: Optimized static hashing engine utilizing SHA-256 with zero-allocation span buffers.
- `SystemTextJsonIdempotencySerializer`: Reflection-free serializer for response caching.
- `IdempotencyJsonContext`: Source-generated `JsonSerializerContext` for 100% Native AOT compatibility.
- `AsyncLocalIdempotencyContextAccessor`: `AsyncLocal<T>`-backed implementation of `IIdempotencyContextAccessor`.
- `IdempotencyDiagnostics`: OpenTelemetry instrumentation emitting `ActivitySource` traces and `Meter` metrics.
- `IdempotencyProblemDetails`: Strongly typed RFC 9110 compliant problem details model for HTTP 400 and 409 idempotency errors.
- `IdempotencyCleanupBackgroundService`: Periodic background worker purging expired idempotency records.

### Dependency Injection
- `ServiceCollectionExtensions`:
  - `AddIdempotencyCore(Action<IdempotencyOptions>?)`: Registers core domain contracts, serializers, and engine.
  - `AddIdempotencyCleanupService(Action<IdempotencyCleanupOptions>?)`: Registers hosted background retention worker.

---

## 3. `EricksonLopez.Idempotency.AspNetCore`

### Middleware, Filters & Attributes
- `IdempotentEndpointFilter`: Minimal API endpoint filter intercepting HTTP requests carrying `Idempotency-Key` headers.
- `IdempotencyMiddleware`: ASP.NET Core middleware for controller actions decorated with `[Idempotent]`.
- `IdempotentAttribute`: Endpoint metadata attribute configuring per-route `Scope`, `LeaseDurationSeconds`, `RetentionDurationDays`, and `Required`.
- `AspNetCoreServiceCollectionExtensions`:
  - `AddAspNetCoreIdempotency(Action<IdempotencyOptions>?)`: Registers filter, middleware dependencies, and core services.
  - `UseIdempotency(IApplicationBuilder)`: Enables idempotency middleware in HTTP pipeline.
  - `WithIdempotency(RouteHandlerBuilder)`: Attaches endpoint filter to a Minimal API route.
  - `WithIdempotency(RouteGroupBuilder)`: Attaches endpoint filter to a Minimal API route group.
- `IdempotencyOptionsAspNetCoreExtensions`:
  - `UseTenantIdExtractor(Func<HttpContext, Guid>)`: Fluent helper configuring strongly typed tenant resolution.

---

## 4. `EricksonLopez.Idempotency.Mediator`

### Pipeline Behaviors & Contracts
- `IIdempotentRequest`: Marker contract exposing `IdempotencyKey` and `TenantId`.
- `IdempotencyPipelineBehavior<TRequest, TResponse>`: Struct-based pipeline behavior for `EricksonLopez.Mediator` enforcing effectively-once execution.
- `MediatorServiceCollectionExtensions`:
  - `AddMediatorIdempotency(IServiceCollection)`: Registers `IdempotencyPipelineBehavior` into mediator pipeline.

---

## 5. `EricksonLopez.Idempotency.Result`

### Functional Extensions & Errors
- `IdempotencyResultExtensions`: Extensions bridging idempotency execution to `Result<T>` (`AsErrorResult<T>()`).
- `IdempotencyErrors`: Factory methods producing structured `Error.Conflict` descriptors (`InFlightConflict`, `FingerprintMismatch`, `LeaseLost`).

---

## 6. Storage Provider Packages

- `EricksonLopez.Idempotency.PostgreSql`: PostgreSQL storage adapter using `NpgsqlDataSource`, Dapper, `ON CONFLICT (tenant_id, scope, idempotency_key) DO NOTHING`, and `ITransactionalIdempotencyStore`.
- `EricksonLopez.Idempotency.SqlServer`: SQL Server storage adapter using `Microsoft.Data.SqlClient`, Dapper, `IF NOT EXISTS ... WITH (UPDLOCK, HOLDLOCK) INSERT`, and `ITransactionalIdempotencyStore`.
- `EricksonLopez.Idempotency.MySql`: MySQL storage adapter using `MySqlConnector`, Dapper, atomic `INSERT IGNORE INTO`, and `ITransactionalIdempotencyStore`.
- `EricksonLopez.Idempotency.MariaDb`: MariaDB storage adapter using `MySqlConnector`, Dapper, atomic `INSERT IGNORE INTO`, and `ITransactionalIdempotencyStore`.
- `EricksonLopez.Idempotency.Oracle`: Oracle Database storage adapter using `Oracle.ManagedDataAccess.Core`, Dapper, `MERGE INTO ... USING DUAL`, and `ITransactionalIdempotencyStore`.
- `EricksonLopez.Idempotency.Sqlite`: SQLite storage adapter using `Microsoft.Data.Sqlite`, Dapper, and atomic `INSERT OR IGNORE INTO`.
- `EricksonLopez.Idempotency.Redis`: Distributed storage using `StackExchange.Redis` with atomic Lua scripts for acquisition and CAS transitions.
- `EricksonLopez.Idempotency.Testing`: In-memory thread-safe `InMemoryIdempotencyStore` for unit and integration testing with `TimeProvider` injection and `Clear()` state reset.
