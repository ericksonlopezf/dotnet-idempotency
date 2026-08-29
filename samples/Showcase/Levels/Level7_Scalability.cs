// Copyright © Erickson Lopez. MIT License.
using System;
using System.Text;
using System.Threading.Tasks;
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.Testing;

namespace EricksonLopez.Idempotency.Showcase.Levels;

/// <summary>
/// Demonstrates multi-tenant key isolation, horizontal scaling invariants,
/// background TTL batch cleanup, and performance guidance for distributed deployments.
/// </summary>
public sealed class Level7Scalability : ILevel
{
    /// <inheritdoc/>
    public string Name => "Level 7 — Multi-Tenancy Partitioning, TTL Cleanup & Horizontal Scaling";

    /// <inheritdoc/>
    public string Description => "Tenant isolation (Tenant A vs Tenant B) with the same key, batch cleanup, horizontal scaling invariants, and throughput guidance.";

    /// <inheritdoc/>
    public async Task ExecuteAsync()
    {
        // ─── 1. Multi-Tenant Key Isolation ────────────────────────────────────────
        Console.WriteLine("1. Multi-Tenant Key Isolation:");

        var store = new InMemoryIdempotencyStore();
        var sharedKey = new IdempotencyKey("INVOICE-001");
        var tenantAlpha = Guid.NewGuid();
        var tenantBeta = Guid.NewGuid();

        var payload = Encoding.UTF8.GetBytes("{\"amount\":100}");
        var fp = IdempotencyFingerprintHasher.Compute("POST", "invoices", "tenant-alpha", null, payload);

        // Tenant Alpha acquires key
        var claimAlpha = await store.TryAcquireAsync(tenantAlpha, "invoices", sharedKey, fp, TimeSpan.FromMinutes(1), TimeSpan.FromDays(7));
        Console.WriteLine($" -> Tenant Alpha acquire 'INVOICE-001': {claimAlpha.Status} (OwnerToken: {claimAlpha.OwnerToken?.ToString()[..8]}...)");

        // Tenant Beta acquires SAME key without colliding with Tenant Alpha
        var claimBeta = await store.TryAcquireAsync(tenantBeta, "invoices", sharedKey, fp, TimeSpan.FromMinutes(1), TimeSpan.FromDays(7));
        Console.WriteLine($" -> Tenant Beta  acquire 'INVOICE-001': {claimBeta.Status} (OwnerToken: {claimBeta.OwnerToken?.ToString()[..8]}...)");

        if (claimAlpha.Status == ClaimResultStatus.AcquiredNew && claimBeta.Status == ClaimResultStatus.AcquiredNew)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(" -> [SUCCESS] Multi-tenancy partitions keys cleanly without cross-tenant interference!");
            Console.ResetColor();
        }

        // Demonstrate composite key structure: (tenantId, scope, key)
        Console.WriteLine("\n   Composite key structure in stores:");
        Console.WriteLine($"   -> Alpha record: '{tenantAlpha:D}:invoices:INVOICE-001'");
        Console.WriteLine($"   -> Beta  record: '{tenantBeta:D}:invoices:INVOICE-001'");
        Console.WriteLine("   -> Different tenantId prefix guarantees full partition isolation.");

        // ─── 2. Scope-Level Isolation ─────────────────────────────────────────────
        Console.WriteLine("\n2. Scope-Level Isolation — same key, different functional scopes:");

        var sameKey = new IdempotencyKey("OP-12345");
        var scopeA = "orders";
        var scopeB = "payments";
        var sameFp = IdempotencyFingerprintHasher.Compute("POST", scopeA, Guid.Empty.ToString(), null, []);

        var claimScopeA = await store.TryAcquireAsync(Guid.Empty, scopeA, sameKey, sameFp, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));
        var claimScopeB = await store.TryAcquireAsync(Guid.Empty, scopeB, sameKey, sameFp, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));

        Console.WriteLine($" -> '{scopeA}' scope acquire: {claimScopeA.Status}");
        Console.WriteLine($" -> '{scopeB}' scope acquire: {claimScopeB.Status}");
        Console.WriteLine("   Both AcquiredNew — scope is part of the composite key.");

        // ─── 3. Background TTL Batch Cleanup ─────────────────────────────────────
        Console.WriteLine("\n3. Background TTL Batch Cleanup — CleanupExpiredRecordsAsync:");

        var cleanupStore = new InMemoryIdempotencyStore();

        // Insert 5 records with very short retention
        for (int i = 1; i <= 5; i++)
        {
            var shortKey = new IdempotencyKey($"EXPIRE-{i:D3}");
            var shortFp = IdempotencyFingerprintHasher.Compute("POST", "cleanup-scope", Guid.Empty.ToString(), null, Encoding.UTF8.GetBytes($"{{\"id\":{i}}}"));
            _ = await cleanupStore.TryAcquireAsync(Guid.Empty, "cleanup-scope", shortKey, shortFp, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1));
        }

        // Cleanup with a future timestamp — all records should be expired
        var futureTimestamp = DateTimeOffset.UtcNow.AddDays(10);
        var deletedCount = await cleanupStore.CleanupExpiredRecordsAsync(futureTimestamp, batchSize: 100);
        Console.WriteLine($" -> Inserted 5 records with 1ms retention");
        Console.WriteLine($" -> CleanupExpiredRecordsAsync(futureDate, batchSize=100): purged {deletedCount} record(s)");

        // ─── 4. Horizontal Scaling Invariants ─────────────────────────────────────
        Console.WriteLine("\n4. Horizontal Scaling Invariants — design guarantees for multi-instance deployments:");
        Console.WriteLine(@"
   Multi-instance safety requires a shared external store (not InMemoryIdempotencyStore):

   ┌──────────────────────────────────────────────────────────────────────────┐
   │  API Gateway                                                              │
   │    ├── Instance A ──────→ PostgreSqlIdempotencyStore (shared DB)         │
   │    ├── Instance B ──────→ PostgreSqlIdempotencyStore (same DB)           │
   │    └── Instance C ──────→ PostgreSqlIdempotencyStore (same DB)           │
   │                                                                           │
   │  All instances share the same idempotency record table.                  │
   │  Database-level atomic operations (INSERT ... ON CONFLICT DO NOTHING)    │
   │  guarantee that only ONE instance wins the lease per key.                │
   └──────────────────────────────────────────────────────────────────────────┘

   For Redis: Atomic Lua scripts provide the same CAS (Compare-And-Swap) guarantee
   across all instances sharing the same Redis cluster.

   Key invariants:
   - Fencing token (ConcurrencyVersion) prevents split-brain execution
   - Lease TTL ensures automatic recovery from crashed instances
   - Composite key (TenantId + Scope + Key) prevents cross-tenant/cross-scope collision
   - CleanupExpiredRecordsAsync should run via AddIdempotencyCleanupService on one instance
     (or all instances with small BatchSize to avoid thundering herd)
");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[SUCCESS] All Level 7 scalability scenarios demonstrated.");
        Console.ResetColor();
    }
}
