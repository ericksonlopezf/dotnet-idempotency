# EricksonLopez.Idempotency — Frequently Asked Questions

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## General

### What is idempotency and why does this library exist?

Idempotency guarantees that performing the same logical operation multiple times produces **at most one observable side effect**. In distributed systems, network timeouts, client retries, broker redeliveries, and infrastructure failovers are normal — without idempotency they cause duplicate payments, double-created orders, or corrupt domain state. This library provides a production-grade, low-allocation engine for enforcing that guarantee across ASP.NET Core, CQRS pipelines, background workers, and message consumers.

---

### What's the difference between idempotency, transactions, and the outbox pattern?

```
Idempotency   ≠   Transactions   ≠   Outbox
("same logical    ("atomic batch     ("publish event
  operation?")     of mutations?")    safely after commit?")
```

They are complementary, not interchangeable. This library handles idempotency (de-duplication), and optionally participates in transactions via `ITransactionalIdempotencyStore` to coordinate the three concerns atomically.

---

### Does this library work without ASP.NET Core?

Yes. `EricksonLopez.Idempotency` (the core package) and `EricksonLopez.Idempotency.Abstractions` have no dependency on ASP.NET Core. The `IdempotencyEngine` can be used directly in console apps, Worker Services, Lambda functions, and message consumers. `EricksonLopez.Idempotency.AspNetCore` is an optional add-on.

---

### Is this compatible with Native AOT?

Yes — the library is built with a Native AOT–first philosophy:
- All value objects use `readonly record struct` (no boxing).
- SHA-256 fingerprinting uses stack-allocated `Span<byte>` buffers.
- JSON serialization uses source-generated `IdempotencyJsonContext : JsonSerializerContext` (no reflection).
- All `System.Text.Json` paths use `JsonSerializerOptions` overloads compatible with AOT trimming.

---

## Configuration

### What is `IdempotencyOptions` and how do I configure it?

`IdempotencyOptions` is the central configuration object registered via `AddIdempotencyCore(options => { ... })` or `AddAspNetCoreIdempotency(options => { ... })`. Key properties:

| Property | Default | Description |
|---|---|---|
| `HeaderName` | `"Idempotency-Key"` | HTTP header carrying the key |
| `DefaultLeaseDuration` | 30 seconds | How long a lock is held before becoming stealable |
| `DefaultRetentionDuration` | 7 days | How long completed records are kept for replay |
| `CacheOnlySuccessResponses` | `true` | Only cache 2xx responses; transient errors are retriable |
| `RequireIdempotencyKey` | `false` | Return 400 if header is missing |

---

### How do I configure per-endpoint options (scope, duration)?

Use `IdempotentAttribute` on a Minimal API route or MVC action:

```csharp
app.MapPost("/api/orders", Handler)
   .WithIdempotency()
   .WithMetadata(new IdempotentAttribute
   {
       Scope = "orders",
       LeaseDurationSeconds = 30,
       RetentionDurationDays = 7
   });
```

---

### What is `IdempotencyScope` and when should I set it?

`IdempotencyScope` logically partitions keys across business operations. Without a scope, a key `"OP-001"` used in `orders` and `payments` would be the same record. Scope separates them: `(tenant, "orders", "OP-001")` ≠ `(tenant, "payments", "OP-001")`. Set scope to the domain operation name.

---

## Keys and Fingerprinting

### What is `IdempotencyKey` and what values are valid?

`IdempotencyKey` is a `readonly record struct` wrapping a string. Creation:

```csharp
var key = new IdempotencyKey("my-key-value");
var key = IdempotencyKey.Create(correlationId);     // from Guid
var key = IdempotencyKey.CreateNew();               // random UUID
bool ok = IdempotencyKey.TryParse(rawString, out var key);
```

The value must be a non-null, non-empty string. UUIDs are recommended for uniqueness.

---

### What is fingerprinting and when does a mismatch occur?

A fingerprint is a deterministic SHA-256 hash of: `(operationName, scope, tenantId, authenticatedSubject, payloadBytes)`. When the same `IdempotencyKey` is used but the payload hash differs, `TryAcquireAsync` returns `ClaimResultStatus.FingerprintMismatch` and the engine throws `IdempotencyFingerprintMismatchException`. This protects against tampered retries.

```csharp
var fp = IdempotencyFingerprintHasher.Compute(
    "POST", "orders", tenantId.ToString(), userId,
    requestBodyBytes);
```

---

### Can I disable fingerprint checking?

Not via configuration — fingerprinting is a core safety guarantee. To bypass checking, implement a custom `IIdempotencyFingerprintGenerator` that always returns the same value (e.g., empty string). This is not recommended in production.

---

## Stores

### Which store should I use?

| Store | Use case |
|---|---|
| `InMemoryIdempotencyStore` | Unit tests, local dev, single-process |
| `PostgreSqlIdempotencyStore` | Production — PostgreSQL with `ON CONFLICT DO NOTHING` |
| `SqlServerIdempotencyStore` | Production — SQL Server with `MERGE WITH HOLDLOCK` |
| `MySqlIdempotencyStore` | Production — MySQL with `INSERT IGNORE` |
| `MariaDbIdempotencyStore` | Production — MariaDB with `INSERT IGNORE` |
| `OracleIdempotencyStore` | Production — Oracle with `MERGE INTO ... USING DUAL` |
| `SQLiteIdempotencyStore` | Local dev, embedded, integration tests |
| `RedisIdempotencyStore` | High-throughput, low-latency, edge scenarios |

---

### Can I use `InMemoryIdempotencyStore` in production?

No. It does not persist across restarts and provides no cross-process coordination. Use it only for unit tests and local development. For integration tests with real database schemas, use `SQLiteIdempotencyStore`.

---

### Which stores support `ITransactionalIdempotencyStore`?

PostgreSQL, SQL Server, MySQL, MariaDB, and Oracle. Redis and SQLite do **not** implement `ITransactionalIdempotencyStore`. SQLite can still participate in transactions if you manage the connection externally, but the interface overloads are not available.

---

## Exceptions

### What exceptions does the engine throw?

| Exception | When |
|---|---|
| `IdempotencyConflictException` | Duplicate request while first is in-flight (409 Conflict) |
| `IdempotencyFingerprintMismatchException` | Same key, different payload (409 Conflict) |
| `IdempotencyLeaseExpiredException` | Operation exceeded lease duration (internal, rarely surfaces) |
| `IdempotencyException` | Base class; catch for generic handling |

---

### Should I catch `IdempotencyConflictException`?

In background workers and message consumers, yes — to decide whether to skip, retry, or dead-letter. In ASP.NET Core, the `IdempotentEndpointFilter` and `IdempotencyMiddleware` automatically convert these to `IdempotencyProblemDetails` and return 409 responses.

---

## Testing

### How do I test idempotency in unit tests?

```csharp
var store = new InMemoryIdempotencyStore();
var engine = new IdempotencyEngine(
    store,
    new DefaultIdempotencyPolicy(new IdempotencyOptions()),
    new SystemTextJsonIdempotencySerializer(),
    new AsyncLocalIdempotencyContextAccessor(),
    NullLogger<IdempotencyEngine>.Instance);
```

Use `store.Clear()` between test cases to reset state. Use `new InMemoryIdempotencyStore(fakeTimeProvider)` for deterministic TTL testing.

---

### How do I simulate a zombie lease / TTL expiry in tests?

```csharp
var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow);
var store = new InMemoryIdempotencyStore(fakeTime);

// Acquire with 1-second lease
await store.TryAcquireAsync(..., leaseDuration: TimeSpan.FromSeconds(1), ...);

// Advance clock — lease is now expired
fakeTime.Advance(TimeSpan.FromSeconds(5));

// Next acquire returns AcquiredStale (lease stolen)
var result = await store.TryAcquireAsync(...);
Assert.Equal(ClaimResultStatus.AcquiredStale, result.Status);
```

---

## Performance

### What is the memory allocation profile?

The library is designed for near-zero allocation in the hot path:
- `IdempotencyKey` and `IdempotencyScope` are `readonly record struct` (stack-allocated).
- SHA-256 fingerprinting uses `stackalloc byte[]` (no heap allocation under 256 bytes).
- `IdempotencyClaimResult` is a `record` (single heap allocation per request).
- JSON serialization uses source-generated context — no reflection, no dynamic code gen.

---

### When should I use Redis instead of a relational store?

Use `RedisIdempotencyStore` when:
- Idempotency keys are short-lived (hours, not days).
- You need sub-millisecond store latency at extreme throughput (>10k req/s).
- You can tolerate the absence of `ITransactionalIdempotencyStore` (no atomic outbox).

Use a relational store when:
- You need long-retention idempotency records (days to months).
- You need the atomic outbox pattern.
- You need SQL query auditability.

---

## Observability

### What OpenTelemetry signals does the library emit?

The library emits signals via `IdempotencyDiagnostics`:
- **ActivitySource**: distributed traces for each idempotency execution.
- **Meter**: counters for `requests`, `executions`, `completed`, `replayed`, `duplicates`, `conflicts`, `failed`, `fingerprint_mismatch`; histograms for `duration` and `storage_latency`.

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(IdempotencyDiagnostics.ServiceName))
    .WithMetrics(m => m.AddMeter(IdempotencyDiagnostics.ServiceName));
```
