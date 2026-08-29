# Level 02: Complete Configuration & Options

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Overview

Level 02 details the complete configuration surface of `EricksonLopez.Idempotency`, including dependency injection registration, lease tuning, payload buffering limits, and background cleanup scheduling.

---

## 2. Dependency Injection Registration

Use `services.AddIdempotencyCore(...)` to register the core engine services in your application's `IServiceCollection`:

```csharp
using System;
using EricksonLopez.Idempotency;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// 1. Configure Idempotency Core Options
services.AddIdempotencyCore(options =>
{
    // HTTP header name for the idempotency key (Default: "Idempotency-Key")
    options.HeaderName = "X-Idempotency-Key";

    // Duration an in-flight execution holds exclusive lease (Default: 30s)
    options.DefaultLeaseDuration = TimeSpan.FromSeconds(45);

    // Retention duration before completed records are purged by cleanup (Default: 7 days)
    options.DefaultRetentionDuration = TimeSpan.FromDays(14);

    // Maximum request body size buffered in memory for fingerprint calculation (Default: 1 MB)
    options.MaxRequestBodySizeBytes = 2 * 1024 * 1024; // 2 MB

    // Whether requests without an Idempotency-Key header should be rejected with 400 Bad Request
    options.RequireIdempotencyKey = true;

    // Whether response body bytes should be cached in storage (Default: true)
    options.StoreResponseBody = true;

    // When true (default), only HTTP 2xx responses are cached.
    // When false, 4xx and 5xx responses are also cached and replayed.
    options.CacheOnlySuccessResponses = true;

    // Custom TenantId extraction delegate
    options.TenantIdExtractor = context =>
    {
        if (context.Request.Headers.TryGetValue("X-Tenant-ID", out var tenantHeader) &&
            Guid.TryParse(tenantHeader.ToString(), out var tenantGuid))
        {
            return tenantGuid;
        }
        return Guid.Empty;
    };
});

// 2. Register Periodic Background TTL Cleanup Service
services.AddIdempotencyCleanupService(cleanup =>
{
    // Interval between background cleanup runs (Default: 1 hour)
    cleanup.Interval = TimeSpan.FromMinutes(30);

    // Batch size per cleanup deletion batch (Default: 500 records)
    cleanup.BatchSize = 1000;
});
```

---

## 3. Configuration Reference Matrix

| Option Property | Type | Default | Description |
|---|---|---|---|
| `HeaderName` | `string` | `"Idempotency-Key"` | The HTTP request header checked for the client's idempotency key. |
| `DefaultLeaseDuration` | `TimeSpan` | `30s` | Maximum time an in-flight operation can run before its lease is considered stale and subject to stealing. |
| `DefaultRetentionDuration` | `TimeSpan` | `7 days` | Duration completed records remain in storage before automated TTL cleanup deletes them. |
| `MaxRequestBodySizeBytes` | `long` | `1,048,576` (1MB) | Maximum payload size allowed for SHA-256 fingerprint hashing to prevent memory exhaustion. |
| `RequireIdempotencyKey` | `bool` | `false` | When `true`, requests missing the key header receive HTTP 400 Bad Request. |
| `StoreResponseBody` | `bool` | `true` | When `true`, serialized response bodies are persisted for full replay. |
| `CacheOnlySuccessResponses` | `bool` | `true` | When `true`, error status codes (4xx, 5xx) are not cached, allowing safe immediate client retry. |
| `TenantIdExtractor` | `Func<HttpContext, Guid>?` | `null` (auto-detect) | Custom delegate to resolve tenant identifier from HTTP request headers or JWT claims. |

---

## 4. Next Steps

Proceed to [Level 03: Real Use Cases & Payload Mismatch Detection](level-03-real-use-cases.md).
