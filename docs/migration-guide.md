# EricksonLopez.Idempotency — Migration Guide

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## Overview

This guide documents breaking changes, migration steps, and upgrade procedures for `EricksonLopez.Idempotency`. Each section corresponds to a version or a significant API change.

---

## Migrating from 1.0.0 to 2.0.0 (Release 2026-09-23)

### 1. `ITransactionalIdempotencyStore` — New Overloads on MySQL and MariaDB

**What changed**: `EricksonLopez.Idempotency.MySql` and `EricksonLopez.Idempotency.MariaDb` now implement `ITransactionalIdempotencyStore`, adding transaction-aware `MarkCompletedAsync` and `MarkFailedAsync` overloads.

**Impact**: Additive — no breaking change. Existing code using `IIdempotencyStore` continues to work unchanged.

**How to opt in** (atomic outbox pattern):

```csharp
// Before (non-transactional):
await store.MarkCompletedAsync(tenantId, scope, key, ownerToken, version,
    statusCode, headers, body, retention, ct);

// After (transactional — participate in active DB transaction):
if (store is ITransactionalIdempotencyStore txStore)
{
    await txStore.MarkCompletedAsync(tenantId, scope, key, ownerToken, version,
        statusCode, headers, body, retention,
        dbConnection, dbTransaction, ct);
}
```

---

### 2. `IdempotencyOptions.ResponseHeadersBlocklist` — New Security Property

**What changed**: `IdempotencyOptions` now has `ResponseHeadersBlocklist` (case-insensitive `HashSet<string>`). By default it blocks: `Set-Cookie`, `Set-Cookie2`, `Authorization`, `Proxy-Authenticate`, `Proxy-Authorization`, `WWW-Authenticate`.

**Impact**: Responses that previously cached these headers will now have them stripped before storage. This is a behavioral change for anyone relying on replayed `Set-Cookie` headers.

**Migration**:
```csharp
// To restore old behavior (not recommended):
options.ResponseHeadersBlocklist.Clear();

// To add custom headers to block:
options.ResponseHeadersBlocklist.Add("X-Internal-Token");
```

---

### 3. `IdempotencyKey` — Restored `explicit operator`

**What changed**: `explicit operator IdempotencyKey(string value)` was restored.

**Migration**: Code that previously used `new IdempotencyKey(value)` continues to work. You can now also use:
```csharp
var key = (IdempotencyKey)"my-key"; // explicit cast
```

---

### 4. `IdempotencyOptions.RequireIdempotencyKey` — New Property

**What changed**: When `RequireIdempotencyKey = true`, requests without the `Idempotency-Key` header return HTTP 400 Bad Request with `IdempotencyProblemDetails`.

**Default**: `false` — all existing endpoints without the header continue to pass through unchanged.

**Migration**: No action required unless you want to enforce header presence.

---

### 5. OpenTelemetry Metrics — New Signals in Middleware and Filter

**What changed**: `IdempotencyMiddleware` and `IdempotentEndpointFilter` now automatically record all `IdempotencyDiagnostics` metrics (`requests`, `replayed`, `duplicates`, `conflicts`, `executions`, `completed`, `failed`, `fingerprint_mismatch`, `duration`).

**Impact**: If you have custom metric recording wrapping the middleware, you may now see duplicate metric emissions. Remove manual `IdempotencyDiagnostics.RecordXxx()` calls from custom middleware wrappers.

---

## General Upgrade Checklist

When upgrading `EricksonLopez.Idempotency` between any versions:

1. **Read the CHANGELOG** (`CHANGELOG.md`) for breaking changes in the target version.
2. **Check DI registrations**: All stores register via `TryAddX` — a newer version adding a new default implementation will not override your explicit registrations.
3. **Run the Abstractions test suite**: `dotnet test tests/EricksonLopez.Idempotency.Abstractions.Tests/` — these tests validate the public API surface contracts.
4. **Verify the Showcase compiles**: `dotnet build samples/Showcase/` — the Showcase is the reference implementation and must always compile.
5. **Review `IdempotencyOptions`** for new properties and their defaults — ensure defaults align with your production policy.

---

## Migrating from a Custom Store to a First-Party Store

If you previously implemented a custom `IIdempotencyStore` and now want to switch to a first-party provider:

```csharp
// Remove your custom registration:
// services.AddSingleton<IIdempotencyStore, MyCustomStore>(); // remove this

// Add the first-party provider:
services.AddPostgreSqlIdempotencyStore();   // PostgreSQL
services.AddSqlServerIdempotencyStore(cs);  // SQL Server
services.AddRedisIdempotency(cs);           // Redis
```

**Database schema**: Each first-party store creates its own schema (table, indexes). Refer to each provider package's migration scripts in `docs/` for the exact DDL.

---

## Migrating from `InMemoryIdempotencyStore` to a Production Store

`InMemoryIdempotencyStore` (from `EricksonLopez.Idempotency.Testing`) should never be used in production. To migrate:

```csharp
// Development / tests:
services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();

// Production (swap the registration):
services.AddNpgsqlDataSource(connectionString);
services.AddPostgreSqlIdempotencyStore();
```

All idempotency records will start fresh (in-memory records are not migrated — they are ephemeral by design).

---

## Implementing a Custom Store (SPI)

If you implement `IIdempotencyStore` for a database not covered by first-party packages:

```csharp
public sealed class MyCustomStore : IIdempotencyStore
{
    public Task<IdempotencyClaimResult> TryAcquireAsync(
        Guid tenantId, string scope, IdempotencyKey key, string fingerprint,
        TimeSpan leaseDuration, TimeSpan retentionDuration,
        CancellationToken cancellationToken = default) { ... }

    public Task<bool> MarkCompletedAsync(
        Guid tenantId, string scope, IdempotencyKey key,
        Guid ownerToken, int concurrencyVersion,
        int statusCode, IReadOnlyDictionary<string, string[]> headers,
        ReadOnlyMemory<byte> body, TimeSpan retentionDuration,
        CancellationToken cancellationToken = default) { ... }

    public Task<bool> MarkFailedAsync(
        Guid tenantId, string scope, IdempotencyKey key,
        Guid ownerToken, int concurrencyVersion,
        CancellationToken cancellationToken = default) { ... }

    public Task<long> CleanupExpiredRecordsAsync(
        DateTimeOffset utcNow, int batchSize,
        CancellationToken cancellationToken = default) { ... }
}
```

Optionally implement `ITransactionalIdempotencyStore` for atomic outbox support.

---

## Compatibility Matrix

| Library Version Range | .NET | Native AOT |
|---|---|---|
| Current | net8.0, net9.0, net10.0 | ✅ Full support |

All store providers, serializers, and pipeline behaviors maintain API stability within a major version.
