// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using EricksonLopez.Idempotency;

namespace EricksonLopez.Idempotency.Showcase.Levels;

/// <summary>
/// Demonstrates ASP.NET Core Minimal API endpoint filtering, controller integration,
/// OpenTelemetry distributed tracing and metrics (all IdempotencyDiagnostics methods),
/// and architectural patterns for enterprise deployments.
/// </summary>
public sealed class Level10EnterpriseArchitecture : ILevel
{
    /// <inheritdoc/>
    public string Name => "Level 10 — ASP.NET Core Minimal APIs, OpenTelemetry & Enterprise Patterns";

    /// <inheritdoc/>
    public string Description => "Minimal API endpoint filters, RouteGroupBuilder, controller [Idempotent] attribute, IdempotencyDiagnostics (all metrics), and enterprise architectural patterns.";

    /// <inheritdoc/>
    public Task ExecuteAsync()
    {
        // ─── 1. ASP.NET Core Minimal API & Controller Integration ─────────────────
        Console.WriteLine("1. ASP.NET Core Minimal API & Controller Integration:");
        Console.WriteLine(@"
   // DI Registration:
   services.AddAspNetCoreIdempotency(options =>
   {
       options.HeaderName = ""Idempotency-Key"";
       options.RequireIdempotencyKey = true;
       options.UseTenantIdExtractor(ctx => ctx.User.FindFirst(""tid"") is { Value: var t }
           && Guid.TryParse(t, out var g) ? g : Guid.Empty);
   });
   services.AddPostgreSqlIdempotencyStore();  // NpgsqlDataSource must be registered separately
   services.AddIdempotencyCleanupService(o => { o.Interval = TimeSpan.FromHours(6); o.BatchSize = 500; });

   // Middleware (global — all routes):
   app.UseIdempotency();

   // ──────────────────────────────────────────────────────────────────────────
   // Minimal API — per endpoint:
   app.MapPost(""/api/v1/orders"", async (OrderDto dto, IOrderService service) =>
   {
       var order = await service.CreateOrderAsync(dto);
       return Results.Created($""/api/v1/orders/{order.Id}"", order);
   })
   .WithIdempotency()
   .WithMetadata(new IdempotentAttribute
   {
       Scope = ""orders"",
       LeaseDurationSeconds = 30,
       RetentionDurationDays = 7
   });

   // ──────────────────────────────────────────────────────────────────────────
   // Minimal API — per group (RouteGroupBuilder.WithIdempotency):
   var paymentsGroup = app.MapGroup(""/api/v1/payments"")
                          .WithIdempotency();   // ← applies to ALL routes in group

   paymentsGroup.MapPost(""/charge"",  ChargeHandler);
   paymentsGroup.MapPost(""/refund"",  RefundHandler);
   paymentsGroup.MapPost(""/cancel"",  CancelHandler);

   // ──────────────────────────────────────────────────────────────────────────
   // MVC / Web API Controller:
   [ApiController]
   [Route(""api/v1/payments"")]
   public class PaymentsController : ControllerBase
   {
       [HttpPost]
       [Idempotent(Scope = ""payments"", LeaseDurationSeconds = 60, RetentionDurationDays = 30)]
       public async Task<IActionResult> Charge([FromBody] PaymentRequest request) => Ok();

       [HttpPost(""ping"")]
       [Idempotent(Enabled = false)]  // explicit opt-out for non-idempotent endpoints
       public IActionResult Ping() => Ok();
   }
");

        // ─── 2. IdempotencyDiagnostics — ALL methods ─────────────────────────────
        Console.WriteLine("2. OpenTelemetry Distributed Tracing & Metrics — IdempotencyDiagnostics:");
        Console.WriteLine($" -> Meter Name:           '{IdempotencyDiagnostics.Meter.Name}'");
        Console.WriteLine($" -> ActivitySource Name:  '{IdempotencyDiagnostics.ActivitySource.Name}'");
        Console.WriteLine($" -> ServiceName constant: '{IdempotencyDiagnostics.ServiceName}'");
        Console.WriteLine($" -> ServiceVersion:       '{IdempotencyDiagnostics.ServiceVersion}'");

        // Distributed tracing activity
        using var activity = IdempotencyDiagnostics.ActivitySource.StartActivity("IdempotencyExecution");
        activity?.SetTag("idempotency.scope", "orders");
        activity?.SetTag("idempotency.tenant_id", Guid.Empty.ToString());
        activity?.SetTag("idempotency.replayed", false);
        activity?.SetStatus(ActivityStatusCode.Ok);

        // All metric recording methods
        var scope = "orders";
        IdempotencyDiagnostics.RecordRequest(scope);           // idempotency.requests counter
        IdempotencyDiagnostics.RecordExecution(scope);         // idempotency.executions counter
        IdempotencyDiagnostics.RecordCompleted(scope);         // idempotency.completed counter
        IdempotencyDiagnostics.RecordDuplicate(scope);         // idempotency.duplicates counter
        IdempotencyDiagnostics.RecordReplayed(scope);          // idempotency.replayed counter
        IdempotencyDiagnostics.RecordConflict(scope);          // idempotency.conflicts counter
        IdempotencyDiagnostics.RecordFailed(scope);            // idempotency.failed counter
        IdempotencyDiagnostics.RecordFingerprintMismatch(scope); // idempotency.fingerprint_mismatch counter

        // Histogram methods (end-to-end duration + storage latency)
        IdempotencyDiagnostics.RecordDuration(18.4, scope);             // idempotency.duration histogram (ms)
        IdempotencyDiagnostics.RecordStorageLatency(1.2, "TryAcquire"); // idempotency.storage_latency histogram (ms)

        Console.WriteLine("\n -> All IdempotencyDiagnostics metrics recorded successfully:");
        Console.WriteLine("    Counters: requests, executions, completed, duplicates, replayed,");
        Console.WriteLine("              conflicts, failed, fingerprint_mismatch");
        Console.WriteLine("    Histograms: duration(ms), storage_latency(ms)");

        // ─── 3. OpenTelemetry Registration (external configuration) ───────────────
        Console.WriteLine("\n3. OpenTelemetry Registration (in ASP.NET Core / Worker Service):");
        Console.WriteLine(@"
   builder.Services.AddOpenTelemetry()
       .WithTracing(tracing => tracing
           .AddSource(IdempotencyDiagnostics.ServiceName)  // ← ActivitySource name
           .AddAspNetCoreInstrumentation()
           .AddOtlpExporter())
       .WithMetrics(metrics => metrics
           .AddMeter(IdempotencyDiagnostics.ServiceName)   // ← Meter name
           .AddOtlpExporter());

   // Key metric names for dashboards / alerts:
   // idempotency.requests         — total inbound idempotent requests
   // idempotency.executions       — actual business handler executions (should be << requests)
   // idempotency.replayed         — cache hits (duplicate responses served from store)
   // idempotency.conflicts        — 409 in-flight race conditions
   // idempotency.fingerprint_mismatch — 422 security violation attempts
   // idempotency.failed           — operations that failed and were marked as Failed
   // idempotency.duration         — end-to-end latency histogram (p50, p95, p99)
   // idempotency.storage_latency  — store interaction latency histogram
");

        // ─── 4. Background Cleanup Service ────────────────────────────────────────
        Console.WriteLine("4. Automated Background TTL Cleanup Service:");
        Console.WriteLine(@"
   services.AddIdempotencyCleanupService(options =>
   {
       options.Interval  = TimeSpan.FromHours(6);  // cleanup every 6 hours
       options.BatchSize = 500;                      // purge up to 500 records per cycle
   });

   // Runs as IHostedService (BackgroundService).
   // Calls IIdempotencyStore.CleanupExpiredRecordsAsync(DateTimeOffset.UtcNow, batchSize).
   // Must be used with a registered IIdempotencyStore (AddPostgreSqlIdempotencyStore, etc.).
");

        // ─── 5. Enterprise Architectural Patterns ─────────────────────────────────
        Console.WriteLine("5. Enterprise Architectural Patterns:");
        Console.WriteLine(@"
   ┌──────────────────────────────────────────────────────────────────────────┐
   │  SAGA / Process Manager Pattern                                           │
   │  Each step of a saga carries its own IdempotencyKey.                     │
   │  On saga retry, all already-completed steps are replayed from cache.     │
   │  Only the failed step is re-executed.                                    │
   │                                                                           │
   │  CQRS + Mediator Pattern                                                  │
   │  Command: TransferFundsCommand : IIdempotentRequest                       │
   │  Pipeline: IdempotencyPipelineBehavior → ValidationBehavior → Handler    │
   │  Key is carried on the command; TenantId from domain context.            │
   │                                                                           │
   │  Outbox + Idempotency Atomic Pattern                                      │
   │  BEGIN TX                                                                 │
   │    INSERT domain mutation                                                 │
   │    INSERT outbox event                                                    │
   │    ITransactionalIdempotencyStore.MarkCompletedAsync(..., conn, tx)      │
   │  COMMIT TX                                                                │
   │  → Prevents duplicate domain effects AND duplicate event publication.    │
   │                                                                           │
   │  Message Consumer / Worker Pattern                                        │
   │  var key = IdempotencyKey.Create(messageId);                             │
   │  await engine.ExecuteAsync(tenantId, ""consumer"", key, fp, handler);     │
   │  → Guarantees exactly-once processing of Kafka/SQS/ServiceBus messages.  │
   └──────────────────────────────────────────────────────────────────────────┘
");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[SUCCESS] All Level 10 enterprise scenarios demonstrated.");
        Console.ResetColor();

        return Task.CompletedTask;
    }
}
