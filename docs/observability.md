# Observability, OpenTelemetry & Distributed Tracing

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. OpenTelemetry Instruments

`EricksonLopez.Idempotency` is fully instrumented out of the box with OpenTelemetry Semantic Conventions using .NET `ActivitySource` and `Meter`.

- **ActivitySource Name**: `EricksonLopez.Idempotency`
- **Meter Name**: `EricksonLopez.Idempotency`

---

## 2. Metric Instruments

| Metric Name | Type | Unit | Description | Tags |
|---|---|---|---|---|
| `idempotency.requests` | Counter | `{request}` | Total idempotent operations processed. | `scope` |
| `idempotency.duplicates` | Counter | `{duplicate}` | Total duplicate requests detected. | `scope` |
| `idempotency.replayed` | Counter | `{replay}` | Total cached responses served. | `scope` |
| `idempotency.conflicts` | Counter | `{conflict}` | Total concurrent in-flight collisions. | `scope` |
| `idempotency.executions` | Counter | `{execution}` | Total original business executions. | `scope` |
| `idempotency.completed` | Counter | `{completed}` | Total operations successfully saved. | `scope` |
| `idempotency.failed` | Counter | `{failed}` | Total operations marked failed. | `scope` |
| `idempotency.fingerprint_mismatch` | Counter | `{mismatch}` | Total key reuse payload collisions. | `scope` |
| `idempotency.duration` | Histogram | `ms` | End-to-end operation execution duration. | `scope` |
| `idempotency.storage_latency` | Histogram | `ms` | Store interaction latency. | `operation` |

---

## 3. Distributed Tracing Spans

Every idempotent execution generates structured OpenTelemetry spans:

```text
Span: Idempotency.Execute
 ├── Tag: idempotency.scope = "payments"
 ├── Tag: idempotency.tenant_id = "3fa85f64-5717-4562-b3fc-2c963f66afa6"
 ├── Tag: idempotency.replayed = true / false
 └── Status: Ok / Error
```

---

## 4. OpenTelemetry Registration

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("EricksonLopez.Idempotency")
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddMeter("EricksonLopez.Idempotency")
        .AddOtlpExporter());
```

---

## 5. Consumer-Callable Metric Methods

`IdempotencyDiagnostics` exposes two public methods intended for use by **custom store adapter authors**
and **application-level orchestrators** that bypass `IdempotencyEngine`:

```csharp
IdempotencyDiagnostics.RecordDuration(string scope, double milliseconds);
IdempotencyDiagnostics.RecordStorageLatency(string operation, double milliseconds);
```

> [!NOTE]
> These methods are **not called by the core `IdempotencyEngine`** internally. They exist as a public
> extension point for consumers who:
> - Build custom store adapters and want to emit storage-latency metrics.
> - Write application-layer orchestration code that calls `IIdempotencyStore` directly (without the engine).
> - Instrument integration tests with custom timing.
>
> If you are using the standard `IdempotencyMiddleware`, `IdempotentEndpointFilter`, or `IdempotencyEngine`
> directly, the built-in counters (requests, duplicates, replayed, etc.) are emitted automatically.
> `RecordDuration` and `RecordStorageLatency` are additional instrumentation hooks for advanced scenarios.
