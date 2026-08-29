# Level 10: Enterprise Architecture & Observability

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Overview

Level 10 demonstrates production-grade enterprise integration in ASP.NET Core:
- **Minimal API Endpoint Filter** (`.WithIdempotency()`).
- **Controller Middleware** with `[Idempotent]` attribute.
- **Background Periodic Cleanup** (`AddIdempotencyCleanupService`).
- **OpenTelemetry Observability** (`ActivitySource` and `Meter` metrics).

---

## 2. ASP.NET Core Minimal API Filter

Guarding Minimal API routes is achieved via the `.WithIdempotency()` route extension method:

```csharp
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

// Register idempotency core services and storage
builder.Services.AddIdempotencyCore(options =>
{
    options.HeaderName = "Idempotency-Key";
    options.RequireIdempotencyKey = true;
});
builder.Services.AddPostgreSqlIdempotencyStore(dataSource);

var app = builder.Build();

app.MapPost("/api/v1/orders", async (OrderDto dto, IOrderService service) =>
{
    var order = await service.CreateOrderAsync(dto);
    return Results.Created($"/api/v1/orders/{order.Id}", order);
})
.WithIdempotency()
.WithMetadata(new IdempotentAttribute
{
    Scope = "orders",
    LeaseDurationSeconds = 45,
    RetentionDurationDays = 14
});

app.Run();
```

---

## 3. MVC / Web API Controller Action Middleware

For traditional controller-based APIs, use `app.UseIdempotency()` and decorate action methods with `[Idempotent]`:

```csharp
app.UseRouting();
app.UseIdempotency(); // Register IdempotencyMiddleware
app.MapControllers();

[ApiController]
[Route("api/v1/payments")]
public class PaymentsController : ControllerBase
{
    [HttpPost]
    [Idempotent(Scope = "payments", LeaseDurationSeconds = 60, RetentionDurationDays = 30)]
    public async Task<IActionResult> Charge([FromBody] PaymentRequest request)
    {
        var result = await _paymentService.ProcessChargeAsync(request);
        return Ok(result);
    }

    [HttpPost("ping")]
    [Idempotent(Enabled = false)] // Explicit per-endpoint opt-out
    public IActionResult Ping() => Ok("pong");
}
```

---

## 4. OpenTelemetry Observability (Distributed Tracing & Metrics)

`EricksonLopez.Idempotency` is natively instrumented with zero external collector dependencies:

### Diagnostics Identifiers
- **`ActivitySource` Name**: `"EricksonLopez.Idempotency"`
- **`Meter` Name**: `"EricksonLopez.Idempotency"`

### Available Metrics
| Metric Name | Type | Description |
|---|---|---|
| `idempotency.requests` | Counter | Total number of requests processed by idempotency layer. |
| `idempotency.replayed` | Counter | Number of duplicate requests served from cached replay. |
| `idempotency.conflicts` | Counter | Number of in-flight concurrent execution conflicts (409). |
| `idempotency.mismatches` | Counter | Number of payload fingerprint mismatch validation errors (409/400). |
| `idempotency.duration` | Histogram | Latency distribution (ms) of idempotent execution operations. |

### OpenTelemetry DI Setup
```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("EricksonLopez.Idempotency")
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddMeter("EricksonLopez.Idempotency")
        .AddOtlpExporter());
```

---

## 5. Summary & Verification

You have completed the entire 11-level Showcase journey!  
To run all interactive demonstrations locally:

```bash
dotnet run --project samples/Showcase/EricksonLopez.Idempotency.Showcase.csproj
```
