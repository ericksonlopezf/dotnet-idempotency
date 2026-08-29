// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.Tasks;

namespace EricksonLopez.Idempotency.Showcase.Levels;

/// <summary>
/// Demonstrates the conceptual foundations, architecture, and core guarantees of the idempotency framework.
/// </summary>
public sealed class Level0Conceptual : ILevel
{
    /// <inheritdoc/>
    public string Name => "Level 0 — Conceptual Foundations & Architecture";

    /// <inheritdoc/>
    public string Description => "Core philosophy, distributed systems consistency guarantees, and differentiation from other patterns.";

    /// <inheritdoc/>
    public Task ExecuteAsync()
    {
        Console.WriteLine("╔════════════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                 ERICKSONLOPEZ.IDEMPOTENCY — CORE ARCHITECTURE                  ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("1. What is EricksonLopez.Idempotency?");
        Console.WriteLine("   An enterprise-grade, Native AOT-first architectural framework designed to");
        Console.WriteLine("   guarantee effectively-once execution semantics across HTTP APIs, background");
        Console.WriteLine("   workers, and distributed messaging subscribers.");
        Console.WriteLine();
        Console.WriteLine("2. What problem does it solve?");
        Console.WriteLine("   In distributed networks, retries, client timeouts, and network partitions");
        Console.WriteLine("   inevitably cause duplicate requests. Without architectural idempotency,");
        Console.WriteLine("   retrying an operation causes double-charging, duplicated shipments, and data corruption.");
        Console.WriteLine();
        Console.WriteLine("3. Architectural Separation of Concerns:");
        Console.WriteLine("   ┌────────────────┬─────────────────────────────────────────────────────────┐");
        Console.WriteLine("   │ Architectural  │ Core Question & Guarantee                               │");
        Console.WriteLine("   ├────────────────┼─────────────────────────────────────────────────────────┤");
        Console.WriteLine("   │ Idempotency    │ 'Is this the same logical operation?' (At-most-once)    │");
        Console.WriteLine("   │ Concurrency    │ 'Did the underlying state change?' (Optimistic lock)    │");
        Console.WriteLine("   │ Transactions   │ 'Are these multi-table operations atomic?' (ACID)       │");
        Console.WriteLine("   │ Outbox         │ 'How do we publish events safely?' (Dual-write fix)     │");
        Console.WriteLine("   │ Resilience     │ 'How do we recover from transient faults?' (Retry/CB)   │");
        Console.WriteLine("   │ Result         │ 'How is the business outcome represented?' (Monad)      │");
        Console.WriteLine("   └────────────────┴─────────────────────────────────────────────────────────┘");
        Console.WriteLine();
        Console.WriteLine("4. State Machine Progression:");
        Console.WriteLine("   [AcquiredNew] ──> Executing Handler ──> [CompletedReplay] (Status 200/201)");
        Console.WriteLine("         │                                         │");
        Console.WriteLine("         └── Concurrent Arrival ──> [InFlightConflict] (Status 409)");
        Console.WriteLine();

        return Task.CompletedTask;
    }
}
