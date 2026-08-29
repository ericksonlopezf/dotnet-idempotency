// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.Tasks;
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.AspNetCore;
using Microsoft.Extensions.DependencyInjection;

namespace EricksonLopez.Idempotency.Showcase.Levels;

/// <summary>
/// Demonstrates configuration options, DI registration, and ASP.NET Core / Mediator extension methods.
/// Covers: IdempotencyOptions (all properties), IdempotencyCleanupOptions, AddIdempotencyCore,
/// AddIdempotencyCleanupService, AddAspNetCoreIdempotency, UseIdempotency (shown as code comment
/// since IApplicationBuilder requires a full WebApplication), WithIdempotency, WithIdempotency(group),
/// UseTenantIdExtractor, AddMediatorIdempotency.
/// </summary>
public sealed class Level2Configuration : ILevel
{
    /// <inheritdoc/>
    public string Name => "Level 2 — Complete Configuration & Options";

    /// <inheritdoc/>
    public string Description => "Full configuration surface: IdempotencyOptions, cleanup service, DI registration for Core / AspNetCore / Mediator.";

    /// <inheritdoc/>
    public Task ExecuteAsync()
    {
        // ─── 1. AddIdempotencyCore ────────────────────────────────────────────────
        Console.WriteLine("Step 1: AddIdempotencyCore — complete IdempotencyOptions surface...");

        var services = new ServiceCollection();

        services.AddIdempotencyCore(options =>
        {
            options.HeaderName = "X-Idempotency-Key";
            options.DefaultLeaseDuration = TimeSpan.FromSeconds(45);
            options.DefaultRetentionDuration = TimeSpan.FromDays(14);
            options.MaxRequestBodySizeBytes = 2 * 1024 * 1024;
            options.RequireIdempotencyKey = true;
            options.StoreResponseBody = true;
            options.CacheOnlySuccessResponses = true;

            // TenantIdExtractor (generic object overload — framework-agnostic)
            options.TenantIdExtractor = context =>
            {
                // In non-HTTP contexts (workers, background services), provide tenant directly.
                return context is Guid g ? g : Guid.Empty;
            };
        });

        // ─── 2. AddIdempotencyCleanupService ─────────────────────────────────────
        Console.WriteLine("\nStep 2: AddIdempotencyCleanupService — periodic cleanup background service...");

        services.AddIdempotencyCleanupService(cleanup =>
        {
            cleanup.Interval = TimeSpan.FromHours(1);
            cleanup.BatchSize = 500;
        });

        using var provider = services.BuildServiceProvider();
        var optionsInstance = provider.GetRequiredService<IdempotencyOptions>();

        Console.WriteLine(" -> Options bound successfully:");
        Console.WriteLine($"    - HeaderName:                '{optionsInstance.HeaderName}'");
        Console.WriteLine($"    - DefaultLeaseDuration:       {optionsInstance.DefaultLeaseDuration.TotalSeconds}s");
        Console.WriteLine($"    - DefaultRetentionDuration:   {optionsInstance.DefaultRetentionDuration.TotalDays} days");
        Console.WriteLine($"    - MaxRequestBodySizeBytes:    {optionsInstance.MaxRequestBodySizeBytes:N0} bytes");
        Console.WriteLine($"    - RequireIdempotencyKey:      {optionsInstance.RequireIdempotencyKey}");
        Console.WriteLine($"    - StoreResponseBody:          {optionsInstance.StoreResponseBody}");
        Console.WriteLine($"    - CacheOnlySuccessResponses:  {optionsInstance.CacheOnlySuccessResponses}");
        Console.WriteLine($"    - TenantIdExtractor:          {(optionsInstance.TenantIdExtractor is not null ? "Configured (Custom Delegate)" : "Default (null)")}");

        // ─── 3. AddAspNetCoreIdempotency ─────────────────────────────────────────
        Console.WriteLine("\nStep 3: AddAspNetCoreIdempotency — ASP.NET Core integration...");
        Console.WriteLine(@"
   // Registers core services + IdempotentEndpointFilter (scoped):
   services.AddAspNetCoreIdempotency(options =>
   {
       options.HeaderName = ""Idempotency-Key"";
       options.RequireIdempotencyKey = true;

       // UseTenantIdExtractor: typed HttpContext overload (AspNetCore package)
       options.UseTenantIdExtractor(httpContext =>
       {
           if (httpContext.User.FindFirst(""tid"") is { } claim &&
               Guid.TryParse(claim.Value, out var tenantId))
           {
               return tenantId;
           }
           return Guid.Empty;
       });
   });
");

        // ─── 4. UseIdempotency (middleware) ───────────────────────────────────────
        Console.WriteLine("Step 4: UseIdempotency — HTTP middleware pipeline...");
        Console.WriteLine(@"
   // In Program.cs / Startup.Configure:
   app.UseIdempotency();    // Registers IdempotencyMiddleware in pipeline

   // The middleware:
   //  1. Reads the 'Idempotency-Key' header (or configured HeaderName)
   //  2. Computes request fingerprint from method + path + tenant + body
   //  3. Calls IIdempotencyStore.TryAcquireAsync(...)
   //  4. Returns cached response (HTTP 200/201) without re-executing handler
   //  5. Returns 409 Conflict on in-flight clash
   //  6. Returns 422 Unprocessable on fingerprint mismatch
");

        // ─── 5. WithIdempotency — Minimal API endpoint & group ────────────────────
        Console.WriteLine("Step 5: WithIdempotency — Minimal API endpoint filter and group...");
        Console.WriteLine(@"
   // Per-endpoint (RouteHandlerBuilder):
   app.MapPost(""/api/v1/orders"", handler)
      .WithIdempotency()
      .WithMetadata(new IdempotentAttribute
      {
          Scope = ""orders"",
          LeaseDurationSeconds = 30,
          RetentionDurationDays = 7
      });

   // Per-group (RouteGroupBuilder) — applies to ALL endpoints in group:
   var payments = app.MapGroup(""/api/v1/payments"")
                     .WithIdempotency();
   payments.MapPost(""/charge"",    ChargeHandler);
   payments.MapPost(""/refund"",    RefundHandler);

   // Per-controller action (MVC):
   [Idempotent(Scope = ""payments"", LeaseDurationSeconds = 60, RetentionDurationDays = 30)]
   public async Task<IActionResult> Charge([FromBody] PaymentRequest request) => Ok();

   // Opt-out bypass for a specific endpoint in an idempotent group:
   [Idempotent(Enabled = false)]
   public IActionResult NonIdempotentPing() => Ok();
");

        // ─── 6. AddMediatorIdempotency ────────────────────────────────────────────
        Console.WriteLine("Step 6: AddMediatorIdempotency — Mediator pipeline behavior...");
        Console.WriteLine(@"
   // Registers IdempotencyPipelineBehavior<,> as open generic IPipelineBehavior<,>:
   services.AddMediatorIdempotency();

   // Internally calls AddIdempotencyCore() and registers:
   //   services.TryAddTransient(typeof(IPipelineBehavior<,>),
   //                            typeof(IdempotencyPipelineBehavior<,>));
   //
   // Every ICommand / IQuery that also implements IIdempotentRequest will
   // automatically be guarded by the pipeline behavior.
");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[SUCCESS] All configuration surfaces demonstrated.");
        Console.ResetColor();

        return Task.CompletedTask;
    }
}
