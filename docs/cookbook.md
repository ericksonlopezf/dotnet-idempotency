# EricksonLopez.Idempotency — Practical Production Cookbook

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## Overview

This cookbook provides copy-paste, production-grade recipes for integrating and operating `EricksonLopez.Idempotency`. Every recipe adheres strictly to the library's public API inventory and idiomatic .NET architecture patterns.

---

## Table of Contents

1. [Recipe 1: Minimal API Payment Endpoint with Idempotency Guard](#recipe-1-minimal-api-payment-endpoint-with-idempotency-guard)
2. [Recipe 2: Clean Architecture CQRS Command with Mediator Pipeline Behavior](#recipe-2-clean-architecture-cqrs-command-with-mediator-pipeline-behavior)
3. [Recipe 3: Atomic Outbox Pattern + Idempotency via `ITransactionalIdempotencyStore`](#recipe-3-atomic-outbox-pattern--idempotency-via-itransactionalidempotencystore)
4. [Recipe 4: High-Throughput Redis Caching for Distributed Edge Microservices](#recipe-4-high-throughput-redis-caching-for-distributed-edge-microservices)
5. [Recipe 5: Unit & Integration Testing with `InMemoryIdempotencyStore` & `TimeProvider`](#recipe-5-unit--integration-testing-with-inmemoryidempotencystore--timeprovider)
6. [Recipe 6: Multi-Tenant Key Isolation with Custom Tenant Extractor](#recipe-6-multi-tenant-key-isolation-with-custom-tenant-extractor)
7. [Recipe 7: Automated Background TTL Retention Cleanup Service](#recipe-7-automated-background-ttl-retention-cleanup-service)
8. [Recipe 8: Custom Idempotency Policy for Business Workflows](#recipe-8-custom-idempotency-policy-for-business-workflows)
9. [Recipe 9: Full OpenTelemetry Tracing & Metrics Instrumentation](#recipe-9-full-opentelemetry-tracing--metrics-instrumentation)

---

## Recipe 1: Minimal API Payment Endpoint with Idempotency Guard

### Problem
You need to protect a critical HTTP POST endpoint from duplicate financial charges caused by client retries or network drops.

### Solution
Use `AddAspNetCoreIdempotency()`, register a persistence store (e.g., PostgreSQL), and attach `.WithIdempotency()` with metadata to the route handler.

### Complete Code
```csharp
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.AspNetCore;
using EricksonLopez.Idempotency.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure PostgreSQL DataSource
builder.Services.AddSingleton<NpgsqlDataSource>(_ =>
    NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Postgres")!));

// 2. Register Idempotency Core & PostgreSQL Store
builder.Services.AddAspNetCoreIdempotency(options =>
{
    options.HeaderName = "Idempotency-Key";
    options.RequireIdempotencyKey = true;
    options.DefaultLeaseDuration = TimeSpan.FromSeconds(30);
    options.DefaultRetentionDuration = TimeSpan.FromDays(7);
    options.CacheOnlySuccessResponses = true;
});
builder.Services.AddPostgreSqlIdempotencyStore();

var app = builder.Build();

// 3. Map guarded endpoint
app.MapPost("/api/v1/payments", async (PaymentDto request, IPaymentService paymentService) =>
{
    var confirmation = await paymentService.ProcessPaymentAsync(request);
    return Results.Created($"/api/v1/payments/{confirmation.Id}", confirmation);
})
.WithIdempotency()
.WithMetadata(new IdempotentAttribute
{
    Scope = "payments",
    LeaseDurationSeconds = 45,
    RetentionDurationDays = 14,
    Required = true
});

app.Run();

public sealed record PaymentDto(string AccountId, decimal Amount, string Currency);
public sealed record PaymentConfirmation(string Id, decimal Amount, string Status);
public interface IPaymentService
{
    Task<PaymentConfirmation> ProcessPaymentAsync(PaymentDto dto);
}
```

### Explanation
- `WithIdempotency()` activates the `IdempotentEndpointFilter` on that specific route.
- The filter hashes the HTTP method, path, request body, and tenant into a deterministic SHA-256 fingerprint.
- Duplicate calls with identical keys replay the original status code, headers, and cached payload without executing the inner service delegate again.
- `CacheOnlySuccessResponses = true` ensures that 5xx/4xx errors are not permanently cached, allowing the client to safely retry.

### Best Practices & Pitfalls
- **Best Practice**: Always set `CacheOnlySuccessResponses = true` so transient upstream timeouts do not permanently lock out the client.
- **Common Mistake**: Forgetting `Request.EnableBuffering()` when writing custom filters; `IdempotentEndpointFilter` handles stream rewind automatically.

---

## Recipe 2: Clean Architecture CQRS Command with Mediator Pipeline Behavior

### Problem
You want idempotency enforced at the application boundary (CQRS Command Handler) rather than solely at the HTTP layer, ensuring background consumers and gRPC endpoints also benefit.

### Solution
Implement `IIdempotentRequest` on your command and register `AddMediatorIdempotency()`.

### Complete Code
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.Mediator;
using EricksonLopez.Idempotency.PostgreSql;
using EricksonLopez.Mediator;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// 1. Register Core & Mediator Idempotency
services.AddMediatorIdempotency();
services.AddPostgreSqlIdempotencyStore();

// 2. Define Command implementing IIdempotentRequest
public sealed record CreateOrderCommand(
    Guid TenantId,
    string CustomerId,
    decimal TotalAmount,
    string Key) : IIdempotentRequest, IRequest<OrderResponse>
{
    public IdempotencyKey IdempotencyKey => new(Key);
}

public sealed record OrderResponse(string OrderId, string Status);

public sealed class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, OrderResponse>
{
    public async ValueTask<OrderResponse> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        // Business logic executed exactly once
        await Task.Delay(50, cancellationToken);
        return new OrderResponse(Guid.NewGuid().ToString("N"), "Confirmed");
    }
}
```

### Explanation
- `IdempotencyPipelineBehavior<TRequest, TResponse>` intercepts commands implementing `IIdempotentRequest`.
- It derives the scope from `typeof(TRequest).FullName` and hashes the serialized command.
- If a duplicate command arrives while the first is in-flight, `IdempotencyConflictException` (409) is thrown.

---

## Recipe 3: Atomic Outbox Pattern + Idempotency via `ITransactionalIdempotencyStore`

### Problem
You need to record domain mutations, write an Outbox message, and mark the idempotency record as `Completed` in a single ACID database transaction to avoid dual-write inconsistencies.

### Solution
Cast `IIdempotencyStore` to `ITransactionalIdempotencyStore` and pass the active `IDbConnection` and `IDbTransaction`.

### Complete Code
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Idempotency;
using Npgsql;

public sealed class OrderCheckoutService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly IIdempotencySerializer _serializer;

    public OrderCheckoutService(
        NpgsqlDataSource dataSource,
        IIdempotencyStore idempotencyStore,
        IIdempotencySerializer serializer)
    {
        _dataSource = dataSource;
        _idempotencyStore = idempotencyStore;
        _serializer = serializer;
    }

    public async Task<CheckoutResult> CheckoutAsync(
        Guid tenantId,
        IdempotencyKey key,
        string fingerprint,
        CheckoutCommand command,
        CancellationToken cancellationToken)
    {
        // 1. Try acquire lease against store
        var claim = await _idempotencyStore.TryAcquireAsync(
            tenantId, "checkout", key, fingerprint,
            leaseDuration: TimeSpan.FromSeconds(30),
            retentionDuration: TimeSpan.FromDays(7),
            cancellationToken);

        if (claim.IsReplay && claim.CachedResponse is not null)
        {
            return _serializer.Deserialize<CheckoutResult>(claim.CachedResponse.Body)!;
        }

        // 2. Open DB connection and begin transaction
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // 3. Execute domain mutation
            var orderId = Guid.NewGuid().ToString("N");
            var result = new CheckoutResult(orderId, command.Amount, "Paid");

            // 4. Save Outbox message in the SAME transaction
            // await outboxWriter.WriteAsync(new OrderPaidEvent(orderId), connection, transaction, cancellationToken);

            // 5. Mark Idempotency as completed within the SAME transaction
            if (_idempotencyStore is ITransactionalIdempotencyStore txStore)
            {
                var bodyBytes = _serializer.Serialize(result);
                await txStore.MarkCompletedAsync(
                    tenantId, "checkout", key,
                    claim.OwnerToken!.Value,
                    claim.ConcurrencyVersion!.Value,
                    statusCode: 200,
                    headers: new Dictionary<string, string[]>(),
                    responseBody: bodyBytes,
                    retentionDuration: TimeSpan.FromDays(7),
                    connection: connection,
                    transaction: transaction,
                    cancellationToken: cancellationToken);
            }

            // 6. Commit transaction atomically
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            if (claim.OwnerToken.HasValue && claim.ConcurrencyVersion.HasValue)
            {
                await _idempotencyStore.MarkFailedAsync(
                    tenantId, "checkout", key,
                    claim.OwnerToken.Value,
                    claim.ConcurrencyVersion.Value,
                    CancellationToken.None);
            }
            throw;
        }
    }
}

public sealed record CheckoutCommand(decimal Amount);
public sealed record CheckoutResult(string OrderId, decimal Amount, string Status);
```

---

## Recipe 4: High-Throughput Redis Caching for Distributed Edge Microservices

### Problem
You need high-throughput, low-latency idempotency checks across hundreds of stateless microservice replicas where database write latency is unacceptable.

### Solution
Use `AddRedisIdempotency()` configured with `StackExchange.Redis`.

### Complete Code
```csharp
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.Redis;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

var services = new ServiceCollection();

// 1. Register Redis connection
services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect("redis-cluster.internal:6379,abortConnect=false"));

// 2. Register Core & Redis Idempotency Store
services.AddIdempotencyCore(options =>
{
    options.DefaultLeaseDuration = TimeSpan.FromSeconds(15);
    options.DefaultRetentionDuration = TimeSpan.FromHours(24);
});

services.AddRedisIdempotency(options =>
{
    options.KeyPrefix = "api:idemp:";
});
```

---

## Recipe 5: Unit & Integration Testing with `InMemoryIdempotencyStore` & `TimeProvider`

### Problem
You want deterministic, fast unit and integration tests for idempotent operations without spinning up Docker database containers or Redis instances.

### Solution
Use `InMemoryIdempotencyStore` and inject `Microsoft.Extensions.Time.Testing.FakeTimeProvider`.

### Complete Code
```csharp
using System;
using System.Threading.Tasks;
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.Exceptions;
using EricksonLopez.Idempotency.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

public sealed class IdempotentServiceTests
{
    [Fact]
    public async Task ExpiredLease_IsRecoveredBySecondWorker_WithIncrementedVersion()
    {
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryIdempotencyStore(fakeTime);
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("TEST-KEY-001");
        var fingerprint = "HASH123";

        // Worker 1 acquires 30s lease
        var claim1 = await store.TryAcquireAsync(tenantId, "test", key, fingerprint,
            leaseDuration: TimeSpan.FromSeconds(30), retentionDuration: TimeSpan.FromDays(7));
        Assert.Equal(ClaimResultStatus.AcquiredNew, claim1.Status);
        Assert.Equal(1, claim1.ConcurrencyVersion);

        // Advance time by 31 seconds (lease expires)
        fakeTime.Advance(TimeSpan.FromSeconds(31));

        // Worker 2 reclaims the stale lease
        var claim2 = await store.TryAcquireAsync(tenantId, "test", key, fingerprint,
            leaseDuration: TimeSpan.FromSeconds(30), retentionDuration: TimeSpan.FromDays(7));

        Assert.Equal(ClaimResultStatus.AcquiredStale, claim2.Status);
        Assert.True(claim2.IsAcquired);
        Assert.Equal(2, claim2.ConcurrencyVersion);
        Assert.NotEqual(claim1.OwnerToken, claim2.OwnerToken);
    }
}
```

---

## Recipe 6: Multi-Tenant Key Isolation with Custom Tenant Extractor

### Problem
In multi-tenant SaaS environments, Tenant A and Tenant B might use identical IDs (e.g. `INV-001`). You must guarantee complete isolation so Tenant A never receives or blocks Tenant B's records.

### Solution
Configure `UseTenantIdExtractor()` in `IdempotencyOptions`.

### Complete Code
```csharp
using System;
using EricksonLopez.Idempotency.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

services.AddAspNetCoreIdempotency(options =>
{
    // Extract tenant ID from custom HTTP header or JWT claims
    options.UseTenantIdExtractor(httpContext =>
    {
        if (httpContext.Request.Headers.TryGetValue("X-Tenant-ID", out var raw) &&
            Guid.TryParse(raw.ToString(), out var tenantId))
        {
            return tenantId;
        }

        var claim = httpContext.User.FindFirst("tenant_id")?.Value;
        if (claim != null && Guid.TryParse(claim, out var claimTenantId))
        {
            return claimTenantId;
        }

        return Guid.Empty;
    });
});
```

---

## Recipe 7: Automated Background TTL Retention Cleanup Service

### Problem
Idempotency records accumulate continuously over weeks and months, consuming disk space and bloating indexes.

### Solution
Register `AddIdempotencyCleanupService()` alongside your persistent store.

### Complete Code
```csharp
using System;
using EricksonLopez.Idempotency;
using Microsoft.Extensions.DependencyInjection;

services.AddIdempotencyCore();
services.AddPostgreSqlIdempotencyStore();

// Register automated background worker
services.AddIdempotencyCleanupService(options =>
{
    options.Interval = TimeSpan.FromHours(2);
    options.BatchSize = 1000;
});
```

---

## Recipe 8: Custom Idempotency Policy for Business Workflows

### Problem
Certain business operations need customized lease durations (e.g. 5 minutes for heavy report generation) or specific status code caching rules (e.g., only caching 200 and 201).

### Solution
Implement `IIdempotencyPolicy` and replace `DefaultIdempotencyPolicy` in DI.

### Complete Code
```csharp
using System;
using EricksonLopez.Idempotency;
using Microsoft.Extensions.DependencyInjection;

public sealed class HeavyReportIdempotencyPolicy : IIdempotencyPolicy
{
    public TimeSpan LeaseDuration => TimeSpan.FromMinutes(5);
    public TimeSpan RetentionDuration => TimeSpan.FromDays(30);
    public bool AllowRetryOnFailure => true;

    public bool IsCacheableStatusCode(int statusCode) => statusCode is 200 or 201;
}

// In DI Registration:
services.AddIdempotencyCore();
services.AddSingleton<IIdempotencyPolicy, HeavyReportIdempotencyPolicy>();
```

---

## Recipe 9: Full OpenTelemetry Tracing & Metrics Instrumentation

### Problem
You need full observability (distributed traces and real-time metrics) across all idempotency evaluations in Grafana/Prometheus/Jaeger.

### Solution
Subscribe to `IdempotencyDiagnostics.ServiceName` in your OpenTelemetry tracer and meter providers.

### Complete Code
```csharp
using EricksonLopez.Idempotency;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("PaymentService"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddSource(IdempotencyDiagnostics.ServiceName) // "EricksonLopez.Idempotency"
            .AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddMeter(IdempotencyDiagnostics.ServiceName)  // "EricksonLopez.Idempotency"
            .AddOtlpExporter();
    });
```
