# Changelog

All notable changes to `EricksonLopez.Idempotency` will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.1](https://github.com/ericksonlopezf/dotnet-idempotency/compare/v1.0.0...v1.0.1) (2026-08-29)


### 🐛 Bug Fixes

* **aot:** set explicit single-target framework in AOT smoke test and sample executables ([c5a1bc8](https://github.com/ericksonlopezf/dotnet-idempotency/commit/c5a1bc81aabba5ac75b32bcdfd833098d0117bc1))
* **ci:** fix Stryker config file path resolution in mutation testing workflow ([020b70a](https://github.com/ericksonlopezf/dotnet-idempotency/commit/020b70ac1737d264385d3fa0a30786cde70864da))
* **tests:** resolve cleanup service test timing and avoid trx collision ([50be81d](https://github.com/ericksonlopezf/dotnet-idempotency/commit/50be81dbe06ab6f18fdd57b9d542adc0804aa519))


### 📖 Documentation

* update mutation score badge to 99% [skip ci] ([143217f](https://github.com/ericksonlopezf/dotnet-idempotency/commit/143217fc009476b35fef285acc236350856fa1b7))


### ✅ Tests

* **abstractions:** add exhaustive unit tests to achieve 100% mutation score on Abstractions ([2c9710e](https://github.com/ericksonlopezf/dotnet-idempotency/commit/2c9710e34733742c1ed643f7442ae693e3786076))

## [Unreleased]

No unreleased changes at this time.

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
[Unreleased]: https://github.com/ericksonlopezf/dotnet-idempotency/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/ericksonlopezf/dotnet-idempotency/releases/tag/v1.0.0
