// Copyright © Erickson Lopez. MIT License.
using System;
using System.Text;
using System.Threading.Tasks;
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.Exceptions;
using EricksonLopez.Idempotency.Testing;

namespace EricksonLopez.Idempotency.Showcase.Levels;

/// <summary>
/// Demonstrates fault tolerance, crash recovery, stale lease recovery, and the complete
/// exception hierarchy (IdempotencyException → IdempotencyConflictException,
/// IdempotencyFingerprintMismatchException, IdempotencyLeaseExpiredException).
/// </summary>
public sealed class Level6ErrorHandling : ILevel
{
    /// <inheritdoc/>
    public string Name => "Level 6 — Fault Tolerance, Zombie Lease Recovery & Exception Hierarchy";

    /// <inheritdoc/>
    public string Description => "Zombie worker lease TTL expiry, automatic recovery with fencing tokens, and the complete idempotency exception hierarchy.";

    /// <inheritdoc/>
    public async Task ExecuteAsync()
    {
        // ─── 1. Exception Hierarchy ───────────────────────────────────────────────
        Console.WriteLine("1. Exception hierarchy (IdempotencyException base class):");
        Console.WriteLine("   IdempotencyException");
        Console.WriteLine("     ├─ IdempotencyConflictException      (.Key property)");
        Console.WriteLine("     ├─ IdempotencyFingerprintMismatchException (.Key, .ExpectedFingerprint, .ActualFingerprint)");
        Console.WriteLine("     └─ IdempotencyLeaseExpiredException  (.Key property)");

        // Demonstrate construction of each exception
        var baseEx = new IdempotencyException("Base idempotency error message.");
        var conflictEx = new IdempotencyConflictException("TX-CONFLICT");
        var mismatchEx = new IdempotencyFingerprintMismatchException("TX-MISMATCH", "expected-fp-hash", "actual-fp-hash");
        var leaseEx = new IdempotencyLeaseExpiredException("TX-LEASE");

        Console.WriteLine($"\n   IdempotencyException:                 '{baseEx.Message}'");
        Console.WriteLine($"   IdempotencyConflictException.Key:     '{conflictEx.Key}' — {conflictEx.Message}");
        Console.WriteLine($"   FingerprintMismatchException.Key:     '{mismatchEx.Key}'");
        Console.WriteLine($"   FingerprintMismatchException.Expected: '{mismatchEx.ExpectedFingerprint}'");
        Console.WriteLine($"   FingerprintMismatchException.Actual:   '{mismatchEx.ActualFingerprint}'");
        Console.WriteLine($"   IdempotencyLeaseExpiredException.Key: '{leaseEx.Key}' — {leaseEx.Message}");

        // Polymorphic catch pattern
        try { throw conflictEx; }
        catch (IdempotencyException ex) when (ex is IdempotencyConflictException)
        {
            Console.WriteLine($"\n   [catch IdempotencyException + subtype filter] Caught: {ex.GetType().Name}");
        }

        // ─── 2. Zombie Worker Lease Recovery ─────────────────────────────────────
        Console.WriteLine("\n2. Zombie Worker Lease Recovery Scenario (Worker 1 crashes midway):");

        var store = new InMemoryIdempotencyStore();
        var key = new IdempotencyKey("CRASH-RECOVER-01");
        var tenantId = Guid.NewGuid();
        var fp = IdempotencyFingerprintHasher.Compute("POST", "orders", tenantId.ToString(), null, Encoding.UTF8.GetBytes("{\"id\":\"O-1\"}"));

        // Worker 1 acquires key with a very short lease (500ms)
        Console.WriteLine("\n   Step A: Worker 1 acquires lease for 500ms...");
        var claim1 = await store.TryAcquireAsync(tenantId, "orders", key, fp, TimeSpan.FromMilliseconds(500), TimeSpan.FromDays(7));
        Console.WriteLine($"   -> Worker 1 Claim Status:   {claim1.Status}");
        Console.WriteLine($"   -> OwnerToken:              {claim1.OwnerToken}");
        Console.WriteLine($"   -> ConcurrencyVersion:      {claim1.ConcurrencyVersion}");
        Console.WriteLine($"   -> IsAcquired:              {claim1.IsAcquired}");

        // Worker 1 crashes (never calls MarkCompletedAsync)
        Console.WriteLine("\n   Step B: Worker 1 CRASHES (never completes or releases the key).");

        // Worker 2 attempts to claim immediately (should fail with InFlightConflict)
        var prematureClaim = await store.TryAcquireAsync(tenantId, "orders", key, fp, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));
        Console.WriteLine($"\n   Step C: Worker 2 premature attempt before TTL: {prematureClaim.Status} (expected InFlightConflict)");

        // Wait for lease to expire
        Console.WriteLine("\n   Step D: Waiting 600ms for lease TTL to expire...");
        await Task.Delay(600);

        // Worker 2 retries — should steal the stale lease with incremented concurrency version
        var recoveryClaim = await store.TryAcquireAsync(tenantId, "orders", key, fp, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));
        Console.WriteLine($"\n   Step E: Worker 2 recovery attempt: {recoveryClaim.Status}");
        Console.WriteLine($"   -> New OwnerToken:                {recoveryClaim.OwnerToken}");
        Console.WriteLine($"   -> Monotonically Incremented Version: {recoveryClaim.ConcurrencyVersion} (Previous: {claim1.ConcurrencyVersion})");
        Console.WriteLine($"   -> IsAcquired:                    {recoveryClaim.IsAcquired}");

        if (recoveryClaim.IsAcquired && recoveryClaim.ConcurrencyVersion > claim1.ConcurrencyVersion)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n   [SUCCESS] Orphaned lease safely recovered with incremented fencing token!");
            Console.ResetColor();
        }

        // ─── 3. MarkFailedAsync — AllowRetryOnFailure pattern ────────────────────
        Console.WriteLine("\n3. MarkFailedAsync — error recovery and retry pattern:");

        var store2 = new InMemoryIdempotencyStore();
        var retryKey = new IdempotencyKey("RETRY-KEY-001");
        var retryFp = IdempotencyFingerprintHasher.Compute("POST", "transfers", Guid.Empty.ToString(), null, Encoding.UTF8.GetBytes("{\"amount\":500}"));

        // Acquire
        var retryClaim = await store2.TryAcquireAsync(Guid.Empty, "transfers", retryKey, retryFp, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));
        Console.WriteLine($"   -> Acquired: Status={retryClaim.Status}");

        // Simulate business failure → MarkFailedAsync
        var markedFailed = await store2.MarkFailedAsync(
            Guid.Empty, "transfers", retryKey,
            retryClaim.OwnerToken!.Value, retryClaim.ConcurrencyVersion!.Value);
        Console.WriteLine($"   -> MarkFailedAsync result: {markedFailed}");

        // With DefaultIdempotencyPolicy.AllowRetryOnFailure = true, next worker can re-acquire
        var retryAttempt = await store2.TryAcquireAsync(Guid.Empty, "transfers", retryKey, retryFp, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));
        Console.WriteLine($"   -> Re-acquisition after failure: {retryAttempt.Status} (expected AcquiredStale — failed records are re-acquirable)");
    }
}
