# Getting Started with EricksonLopez.Idempotency

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Introduction

`EricksonLopez.Idempotency` is an enterprise-grade architectural idempotency framework for .NET 10. It guarantees that repeated requests, retries, message redeliveries, and concurrent requests produce **at most one observable side-effect** and safely replay cached outcomes.

---

## 2. Installation

Install the required packages based on your application architecture:

```bash
# Core engine and abstractions
dotnet add package EricksonLopez.Idempotency

# ASP.NET Core endpoint filters and middleware
dotnet add package EricksonLopez.Idempotency.AspNetCore

# PostgreSQL persistence adapter (Dapper + Raw SQL)
dotnet add package EricksonLopez.Idempotency.PostgreSql

# Mediator pipeline integration (Optional)
dotnet add package EricksonLopez.Idempotency.Mediator

# Result monad integration (Optional)
dotnet add package EricksonLopez.Idempotency.Result
```

---

## 3. Quick Start (ASP.NET Core Minimal APIs)

### 1. Register Services

```csharp
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.AspNetCore;
using EricksonLopez.Idempotency.PostgreSql;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure PostgreSQL DataSource and Idempotency Store
builder.Services.AddNpgsqlDataSource(builder.Configuration.GetConnectionString("DefaultConnection")!);
builder.Services.AddPostgreSqlIdempotencyStore();

// 2. Register ASP.NET Core Adapters & Options
builder.Services.AddAspNetCoreIdempotency(options =>
{
    options.HeaderName = "Idempotency-Key";
    options.RequireIdempotencyKey = false;
    options.DefaultLeaseDuration = TimeSpan.FromSeconds(30);
    options.DefaultRetentionDuration = TimeSpan.FromDays(7);
});

var app = builder.Build();
```

### 2. Guard Endpoints with Idempotency

```csharp
app.MapPost("/api/v1/payments", async (PaymentRequest request, PaymentService service) =>
{
    var payment = await service.ProcessPaymentAsync(request);
    return Results.Ok(payment);
})
.WithIdempotency()
.WithMetadata(new IdempotentAttribute { Scope = "payments", LeaseDurationSeconds = 60, RetentionDurationDays = 14 });

app.Run();
```

---

## 4. Quick Start (Application Layer & Engine)

You can also execute idempotent operations programmatically inside your domain/application services:

```csharp
public sealed class CreateOrderHandler
{
    private readonly IdempotencyEngine _engine;
    private readonly IOrderRepository _repository;

    public CreateOrderHandler(IdempotencyEngine engine, IOrderRepository repository)
    {
        _engine = engine;
        _repository = repository;
    }

    public async Task<OrderResponse> HandleAsync(
        Guid tenantId,
        IdempotencyKey key,
        CreateOrderCommand command,
        CancellationToken cancellationToken)
    {
        var fingerprint = IdempotencyFingerprintHasher.Compute(
            "CreateOrder",
            "orders",
            tenantId.ToString(),
            command.CustomerId.ToString(),
            command.PayloadBytes);

        return await _engine.ExecuteAsync(
            tenantId: tenantId,
            scope: "orders",
            key: key,
            fingerprint: fingerprint,
            operation: async ct =>
            {
                var order = Order.Create(command.CustomerId, command.Amount);
                await _repository.SaveAsync(order, ct);
                return new OrderResponse(order.Id, order.Status);
            },
            cancellationToken: cancellationToken);
    }
}
```

---

## 5. Next Steps

- Explore [Architecture Guide](architecture.md) for architectural patterns and design principles.
- Learn about [Idempotency Key Design](idempotency-key.md) and [Fingerprinting](fingerprinting.md).
- Read [PostgreSQL Persistence](postgresql.md) to set up database schemas and table partitioning.
- See [Mediator Integration](mediator-integration.md) and [Result Integration](result-integration.md).
