# Changelog

All notable changes to `EricksonLopez.Idempotency` will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
 
## [2.0.0] - 2026-09-24

### Breaking Changes

- **Default Response Header Stripping (`BC-001` / `SEC-001`)**:
  - **Change**: `IdempotencyOptions.ResponseHeadersBlocklist` now blocks sensitive HTTP response headers by default (`Set-Cookie`, `Set-Cookie2`, `Authorization`, `Proxy-Authenticate`, `Proxy-Authorization`, `WWW-Authenticate`).
  - **Previous State**: In `v1.0.0`, all non-pseudo headers were persisted and replayed, including session cookies and authorization tokens.
  - **Current State**: Headers matching `ResponseHeadersBlocklist` (case-insensitive) are stripped before storage in `IdempotencyMiddleware` and `IdempotentEndpointFilter`.
  - **Affected Consumers**: Clients or services relying on receiving cached `Set-Cookie` or `Authorization` headers on replayed idempotent requests.
  - **Migration**: To restore the `v1.0.0` behavior of persisting all headers, clear the blocklist in configuration:
    ```csharp
    services.AddAspNetCoreIdempotency(options =>
    {
        options.ResponseHeadersBlocklist.Clear();
    });
    ```

- **Uniform Minimal API Metadata & Scope Resolution (`BC-002` / `ADR-018`)**:
  - **Change**: `IdempotentEndpointFilter` now resolves and prioritizes `IdempotentAttribute` metadata (`Scope`, `LeaseDurationSeconds`, `RetentionDurationDays`, `Required`, `Enabled`) configured on Minimal API routes.
  - **Previous State**: In `v1.0.0`, Minimal API routes attached with `.WithIdempotency()` completely ignored attached `[Idempotent]` metadata attributes; the scope was hardcoded to `httpContext.Request.Path.Value`, and opt-out via `[Idempotent(Enabled = false)]` was ignored.
  - **Current State**: If an endpoint has `[Idempotent(Scope = "...")]`, the attribute scope takes precedence over the URL path. If `[Idempotent(Enabled = false)]` is present, idempotency is bypassed.
  - **Affected Consumers**: Applications that decorated Minimal API endpoints with `.WithMetadata(new IdempotentAttribute { Scope = "custom.scope" })` while running on `v1.0.0`.
  - **Migration**: Existing idempotency keys cached under `v1.0.0` using the URL path will not match lookups under the new custom scope. Wait for in-flight requests and retention windows to expire before upgrading, or align attribute `Scope` values with endpoint route paths.

- **Dynamic Null/Property Suppression in `SystemTextJsonIdempotencySerializer` (`BC-003`)**:
  - **Change**: Added a `DefaultJsonTypeInfoResolver` modifier in `SystemTextJsonIdempotencySerializer` that dynamically excludes the `Error` property when `IsSuccess == true` and excludes the `Value` property when `IsFailure == true` on types implementing Result patterns.
  - **Previous State**: In `v1.0.0`, standard `System.Text.Json` serialization output included all declared properties (e.g., `"error": null` or `"value": null`).
  - **Current State**: The inactive property branch is excluded from the serialized JSON payload.
  - **Affected Consumers**: Downstream consumers, client parsers, or storage queries that expect invariant JSON schemas with explicit `null` fields for inactive properties.
  - **Migration**: If invariant `null` fields are strictly required by external consumers, inject custom `JsonSerializerOptions` into `SystemTextJsonIdempotencySerializer` without the conditional type modifier, or register a custom `IIdempotencySerializer`.

- **RFC 9110 400 Bad Request on Malformed Idempotency Keys in Minimal APIs (`BC-004`)**:
  - **Change**: `IdempotentEndpointFilter` now catches `ArgumentException` and `ArgumentOutOfRangeException` thrown during `IdempotencyKey` instantiation (e.g. whitespace-only or >128 characters) and returns an RFC 9110 HTTP 400 Bad Request Problem Details (`Results.Problem`).
  - **Previous State**: In `v1.0.0`, invalid or oversized keys produced unhandled exceptions resulting in HTTP 500 Internal Server Error.
  - **Current State**: Produces HTTP 400 Bad Request with machine-readable Problem Details.
  - **Affected Consumers**: API clients or test suites asserting HTTP 500 on malformed idempotency headers.
  - **Migration**: Update automated test assertions and client-side error handling to expect HTTP 400 Bad Request for malformed idempotency keys.

### Added

- **Relational Outbox Support for MySQL & MariaDB (`STORE-001`)**:
  - `MySqlIdempotencyStore` and `MariaDbIdempotencyStore` now implement `ITransactionalIdempotencyStore`, exposing `MarkCompletedAsync` and `MarkFailedAsync` overloads accepting `IDbConnection` and `IDbTransaction?`.
  - Added `services.TryAddSingleton<ITransactionalIdempotencyStore>(...)` in `MySqlServiceCollectionExtensions` and `MariaDbServiceCollectionExtensions`.
- **Security & Header Sanitization (`SEC-001`)**:
  - Added `IdempotencyOptions.ResponseHeadersBlocklist` with default case-insensitive blocking of `Set-Cookie`, `Set-Cookie2`, `Authorization`, `Proxy-Authenticate`, `Proxy-Authorization`, and `WWW-Authenticate`.
- **Observability in ASP.NET Core (`OBS-001`)**:
  - Wired full OpenTelemetry metrics emission (`requests`, `replayed`, `duplicates`, `conflicts`, `executions`, `completed`, `failed`, `fingerprint_mismatch`, `duration`) into `IdempotencyMiddleware` and `IdempotentEndpointFilter`.
- **Architecture Decision Record (`ADR-018`)**:
  - Added `docs/adr/adr-018-uniform-endpoint-metadata-resolution.md` documenting uniform endpoint metadata precedence across MVC and Minimal APIs.

### Fixed

- **Concurrency Test Flakiness (`BUG-002`)**:
  - Replaced blocking thread-pool-exhausting `Barrier(60)` with non-blocking async `TaskCompletionSource` in `InMemoryStoreUnitTests`.
- **SQL Server Transaction Abortion (`DB-001`)**:
  - Added defensive `IF NOT EXISTS (SELECT 1 FROM idempotency_records WITH (UPDLOCK, HOLDLOCK) ...)` probe in `SqlServerIdempotencyStore` to prevent ambient transaction doom under `SET XACT_ABORT ON`.
- **Chaos Resilience (`CHAOS-001`)**:
  - Added explicit `cancellationToken.ThrowIfCancellationRequested()` guards across all methods in `InMemoryIdempotencyStore`.

---

## [1.0.0] - 2026-08-29

### Added

- **Core Engine & Abstractions**:
  - `IdempotencyKey` and `IdempotencyScope` value objects with length invariants, ordinal comparison, and convenience factory helpers (`Empty`, `IsEmpty`, `Create(Guid)`, `NewKey()`, `TryParse(string, out IdempotencyKey)`).
  - `IIdempotencyStore` persistence SPI with atomic `TryAcquireAsync`, `MarkCompletedAsync`, `MarkFailedAsync`, and `CleanupExpiredRecordsAsync`.
  - `ITransactionalIdempotencyStore` SPI — extends `IIdempotencyStore` with `MarkCompletedAsync` and `MarkFailedAsync` overloads accepting `IDbConnection`/`IDbTransaction?`, enabling Outbox + Idempotency atomic transactional patterns (ADR-011).
  - `IIdempotencyFingerprintGenerator` SPI — pluggable fingerprint computation strategy with built-in zero-allocation `IdempotencyFingerprintHasher` producing canonical uppercase SHA-256 digests.
  - `IdempotencyEngine` orchestrator with state machine handling, distributed lock fencing tokens, and automatic response replay.
  - `SystemTextJsonIdempotencySerializer` and source-generated `IdempotencyJsonContext` for 100% Native AOT compatibility.
  - Ambient execution context propagation via `AsyncLocalIdempotencyContextAccessor` and `IdempotencyContext`.
  - Native OpenTelemetry instrumentation with `ActivitySource` ("EricksonLopez.Idempotency") and `Meter` ("EricksonLopez.Idempotency") counters/histograms in `IdempotencyDiagnostics`.
  - `IdempotencyOptions` configuration with `CacheOnlySuccessResponses` (default `true`), `Enabled` global kill-switch, and pluggable `TenantIdExtractor` (`Func<object, Guid>?`).
  - `IdempotencyCleanupBackgroundService` — AOT-safe `BackgroundService` for periodic cleanup of expired records using high-performance `[LoggerMessage]`.
  - Multi-target framework support targeting `net8.0`, `net9.0`, and `net10.0`.
  - Strong-name assembly signing enabled across all ecosystem packages.

- **ASP.NET Core Integration (`EricksonLopez.Idempotency.AspNetCore`)**:
  - `IdempotentEndpointFilter` for ASP.NET Core Minimal APIs with `.WithIdempotency()` route extension.
  - `IdempotencyMiddleware` with `[Idempotent]` attribute for MVC and API controller actions.
  - `[Idempotent(Enabled = false)]` — per-endpoint opt-out without removing attribute decoration.
  - `IdempotencyOptionsAspNetCoreExtensions.UseTenantIdExtractor` — fluent extension method for strongly-typed `HttpContext` tenant resolution.
  - Automatic RFC 9110 `application/problem+json` formatting for missing keys (400), in-flight conflicts (409 with `Retry-After`), and payload fingerprint mismatches (409).
  - `AddIdempotencyCleanupService()` DI extension for registering the background cleanup service.

- **Mediator Pipeline Integration (`EricksonLopez.Idempotency.Mediator`)**:
  - `IIdempotentRequest` marker contract exposing `IdempotencyKey` and `TenantId`.
  - `IdempotencyPipelineBehavior<TRequest, TResponse>` for `EricksonLopez.Mediator` — struct-based pipeline behavior providing idempotency guarantees within the mediator pipeline.
  - Multi-tenant CQRS command isolation through composite key matching `(TenantId, Scope, Key)`.

- **Result Monad Integration (`EricksonLopez.Idempotency.Result`)**:
  - `IdempotencyErrors` domain error factories (`InFlightConflict`, `FingerprintMismatch`, `LeaseLost`) for `EricksonLopez.Result`.
  - `IdempotencyResultExtensions.AsErrorResult<T>` extension method.

- **Persistence Providers (Multi-DB SPI Adapters)**:
  - `EricksonLopez.Idempotency.Testing`: In-memory thread-safe `InMemoryIdempotencyStore` supporting `TimeProvider` injection for deterministic unit testing.
  - `EricksonLopez.Idempotency.PostgreSql`: High-performance PostgreSQL persistence using `NpgsqlDataSource`, Dapper, `ON CONFLICT DO NOTHING`, and full `ITransactionalIdempotencyStore` support.
  - `EricksonLopez.Idempotency.SqlServer`: SQL Server persistence provider using SELECT + conditional INSERT with row-level locking, plus full `ITransactionalIdempotencyStore` support.
  - `EricksonLopez.Idempotency.Oracle`: Oracle Database storage provider using `MERGE INTO`, plus full `ITransactionalIdempotencyStore` support.
  - `EricksonLopez.Idempotency.MySql` & `EricksonLopez.Idempotency.MariaDb`: MySQL and MariaDB storage providers using atomic `INSERT IGNORE INTO`.
  - `EricksonLopez.Idempotency.Sqlite`: SQLite storage provider using atomic `INSERT OR IGNORE INTO`.
  - `EricksonLopez.Idempotency.Redis`: Redis storage provider using `StackExchange.Redis` with atomic Lua scripts and source-generated AOT JSON context.

- **Architecture Decision Records**:
  - 17 Architecture Decision Records (ADRs 001–017) in `docs/adr/` documenting design invariants, rejected alternatives, and storage patterns.

- **Showcase & Benchmarks**:
  - Interactive executable Showcase (`EricksonLopez.Idempotency.Showcase`) featuring 11 progressive levels (Levels 00 to 10).
  - BenchmarkDotNet suite (`EricksonLopez.Idempotency.Benchmarks`) evaluating fingerprint hashing throughput, memory footprint, and zero-allocation profiles.

- **Documentation**:
  - Native AOT compatibility matrix with provider-level support table (`docs/aot.md`).
  - `IIdempotencyFingerprintGenerator` customization guide (`docs/fingerprinting.md`).
  - SPI Extension Points guide (`docs/extension-points.md`).
  - Outbox + Idempotency atomic pattern using `ITransactionalIdempotencyStore` (`docs/transaction-integration.md`).
  - Comprehensive cookbook with real-world recipes (`docs/cookbook.md`).

---

<!-- Comparison links -->
[Unreleased]: https://github.com/ericksonlopezf/dotnet-idempotency/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/ericksonlopezf/dotnet-idempotency/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/ericksonlopezf/dotnet-idempotency/releases/tag/v1.0.0
