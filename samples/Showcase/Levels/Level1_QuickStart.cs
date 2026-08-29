// Copyright © Erickson Lopez. MIT License.
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace EricksonLopez.Idempotency.Showcase.Levels;

/// <summary>
/// Demonstrates the quick start workflow, core primitives, and basic execution replay mechanics.
/// Covers: IdempotencyKey (all factory overloads), IdempotencyScope (all factory overloads),
/// IdempotencyEngine, IdempotencyFingerprintHasher, InMemoryIdempotencyStore.
/// </summary>
public sealed class Level1QuickStart : ILevel
{
    /// <inheritdoc/>
    public string Name => "Level 1 — Quick Start & Primitives";

    /// <inheritdoc/>
    public string Description => "IdempotencyKey / IdempotencyScope factories, TryParse, operators, IdempotencyEngine first use and replay.";

    /// <inheritdoc/>
    public async Task ExecuteAsync()
    {
        // ─── 1. IdempotencyKey — All factory overloads ───────────────────────────
        Console.WriteLine("Step 1: IdempotencyKey — factory overloads and operators...");

        // ctor
        var keyViaCtor = new IdempotencyKey("REQ-ORD-98765");
        Console.WriteLine($" -> [ctor]           '{keyViaCtor.Value}', IsEmpty={keyViaCtor.IsEmpty}");

        // Empty sentinel
        var emptyKey = IdempotencyKey.Empty;
        Console.WriteLine($" -> [Empty]          IsEmpty={emptyKey.IsEmpty}");

        // Create(string)
        var keyViaCreate = IdempotencyKey.Create("REQ-ORD-CREATE");
        Console.WriteLine($" -> [Create(string)] '{keyViaCreate.Value}'");

        // Create(Guid)
        var correlationId = Guid.NewGuid();
        var keyViaGuid = IdempotencyKey.Create(correlationId);
        Console.WriteLine($" -> [Create(Guid)]   '{keyViaGuid.Value}' (derived from Guid)");

        // NewKey()
        var randomKey = IdempotencyKey.NewKey();
        Console.WriteLine($" -> [NewKey()]       '{randomKey.Value}' (cryptographically random)");

        // TryParse — success path
        if (IdempotencyKey.TryParse("PAYMENT-TX-001", out var parsedKey))
        {
            Console.WriteLine($" -> [TryParse OK]   '{parsedKey.Value}'");
        }

        // TryParse — failure path (null)
        bool failedParse = IdempotencyKey.TryParse(null, out _);
        Console.WriteLine($" -> [TryParse null]  returned={failedParse} (expected False)");

        // Implicit operator to string
        string keyString = keyViaCtor;
        Console.WriteLine($" -> [implicit→string] '{keyString}'");

        // Explicit operator from string
        var keyFromExplicit = (IdempotencyKey)"EXPLICIT-CAST-001";
        Console.WriteLine($" -> [explicit cast]   '{keyFromExplicit.Value}'");

        // ─── 2. IdempotencyScope — All factory overloads ─────────────────────────
        Console.WriteLine("\nStep 2: IdempotencyScope — factory overloads...");

        // Default sentinel
        var defaultScope = IdempotencyScope.Default;
        Console.WriteLine($" -> [Default]        '{defaultScope.Value}'");

        // ctor
        var scopeViaCtor = new IdempotencyScope("orders");
        Console.WriteLine($" -> [ctor]           '{scopeViaCtor.Value}'");

        // Create(string)
        var scopeViaCreate = IdempotencyScope.Create("payments");
        Console.WriteLine($" -> [Create]         '{scopeViaCreate.Value}'");

        // Implicit operator to string
        string scopeString = scopeViaCtor;
        Console.WriteLine($" -> [implicit→string] '{scopeString}'");

        // ─── 3. Full engine execution cycle ──────────────────────────────────────
        Console.WriteLine("\nStep 3: IdempotencyEngine — first execution and deterministic replay...");

        var store = new InMemoryIdempotencyStore();
        var options = new IdempotencyOptions();
        var policy = new DefaultIdempotencyPolicy(options);
        var serializer = new SystemTextJsonIdempotencySerializer();
        var contextAccessor = new AsyncLocalIdempotencyContextAccessor();
        var engine = new IdempotencyEngine(store, policy, serializer, contextAccessor, NullLogger<IdempotencyEngine>.Instance);

        var key = new IdempotencyKey("REQ-ORD-98765");
        var scope = new IdempotencyScope("orders");
        var fingerprint = IdempotencyFingerprintHasher.Compute("POST", scope.Value, "tenant-1", "user-1", Encoding.UTF8.GetBytes("{\"item\":\"Book\",\"price\":29.99}"));

        int executionCount = 0;

        async Task<OrderResult> CreateOrderAsync(CancellationToken ct)
        {
            await Task.Delay(50, ct);
            executionCount++;
            return new OrderResult("ORD-98765", "Created", 29.99m, DateTimeOffset.UtcNow);
        }

        Console.WriteLine($"\n -> First call with key '{key}'...");
        var firstResult = await engine.ExecuteAsync(Guid.Empty, scope.Value, key, fingerprint, CreateOrderAsync);
        Console.WriteLine($"    OrderId: {firstResult.OrderId}, Status: {firstResult.Status}, Executions: {executionCount}");

        Console.WriteLine($"\n -> Second call (simulating network retry) with identical key '{key}'...");
        var secondResult = await engine.ExecuteAsync(Guid.Empty, scope.Value, key, fingerprint, CreateOrderAsync);
        Console.WriteLine($"    OrderId: {secondResult.OrderId}, Status: {secondResult.Status}, Executions: {executionCount}");

        if (executionCount == 1)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n[SUCCESS] Operation executed exactly ONCE! Second invocation was deterministically replayed.");
            Console.ResetColor();
        }
        else
        {
            throw new InvalidOperationException("Idempotency violation detected!");
        }
    }

    /// <summary>
    /// Represents the sample order creation outcome.
    /// </summary>
    /// <param name="OrderId">The unique order identifier.</param>
    /// <param name="Status">The order status.</param>
    /// <param name="Amount">The order monetary amount.</param>
    /// <param name="CreatedAt">The creation timestamp.</param>
    public sealed record OrderResult(string OrderId, string Status, decimal Amount, DateTimeOffset CreatedAt);
}
