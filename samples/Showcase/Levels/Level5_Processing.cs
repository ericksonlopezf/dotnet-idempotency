// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.Exceptions;
using EricksonLopez.Idempotency.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace EricksonLopez.Idempotency.Showcase.Levels;

/// <summary>
/// Demonstrates high-concurrency race condition handling, in-flight conflict protection,
/// InMemoryIdempotencyStore TimeProvider injection, Clear() method, and batch processing patterns.
/// </summary>
public sealed class Level5Processing : ILevel
{
    /// <inheritdoc/>
    public string Name => "Level 5 — High Concurrency, Race Conditions & Batch Processing";

    /// <inheritdoc/>
    public string Description => "20-thread race simulation, InMemoryIdempotencyStore.Clear(), TimeProvider injection, and batch idempotency execution patterns.";

    /// <inheritdoc/>
    public async Task ExecuteAsync()
    {
        // ─── 1. High-concurrency race condition ───────────────────────────────────
        Console.WriteLine("1. Simulating 20 concurrent client requests arriving simultaneously for key 'RACE-KEY-1'...");

        var store = new InMemoryIdempotencyStore();
        var options = new IdempotencyOptions();
        var policy = new DefaultIdempotencyPolicy(options);
        var serializer = new SystemTextJsonIdempotencySerializer();
        var contextAccessor = new AsyncLocalIdempotencyContextAccessor();
        var engine = new IdempotencyEngine(store, policy, serializer, contextAccessor, NullLogger<IdempotencyEngine>.Instance);

        var key = new IdempotencyKey("RACE-KEY-1");
        var fp = IdempotencyFingerprintHasher.Compute("POST", "tickets", "tenant-1", null, Encoding.UTF8.GetBytes("{\"seat\":\"A12\"}"));

        int actualBusinessExecutions = 0;
        int successfulResponses = 0;
        int conflictCount = 0;

        var tasks = new List<Task>();

        for (int i = 0; i < 20; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var res = await engine.ExecuteAsync(Guid.Empty, "tickets", key, fp, async ct =>
                    {
                        // Simulate non-trivial business processing
                        await Task.Delay(100, ct);
                        Interlocked.Increment(ref actualBusinessExecutions);
                        return new TicketResult("TICKET-A12", "Confirmed");
                    });

                    Interlocked.Increment(ref successfulResponses);
                }
                catch (IdempotencyConflictException)
                {
                    Interlocked.Increment(ref conflictCount);
                }
            }));
        }

        await Task.WhenAll(tasks);

        Console.WriteLine($" -> Total Concurrent Requests:   20");
        Console.WriteLine($" -> Actual Business Executions:  {actualBusinessExecutions} (Expected: 1)");
        Console.WriteLine($" -> Successful Responses:         {successfulResponses}");
        Console.WriteLine($" -> In-Flight 409 Conflicts:      {conflictCount}");

        if (actualBusinessExecutions == 1)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(" -> [SUCCESS] Zero double-execution under intense multi-threaded race!");
            Console.ResetColor();
        }

        // ─── 2. InMemoryIdempotencyStore.Clear() ─────────────────────────────────
        Console.WriteLine("\n2. InMemoryIdempotencyStore.Clear() — resetting state between test runs:");

        var storeForClear = new InMemoryIdempotencyStore();
        var clearKey = new IdempotencyKey("CLEAR-KEY-001");
        var clearFp = IdempotencyFingerprintHasher.Compute("POST", "test", Guid.Empty.ToString(), null, []);

        var beforeClear = await storeForClear.TryAcquireAsync(Guid.Empty, "test", clearKey, clearFp, TimeSpan.FromSeconds(30), TimeSpan.FromDays(1));
        Console.WriteLine($" -> Before Clear: Status={beforeClear.Status}");

        storeForClear.Clear();   // ← public method on InMemoryIdempotencyStore

        var afterClear = await storeForClear.TryAcquireAsync(Guid.Empty, "test", clearKey, clearFp, TimeSpan.FromSeconds(30), TimeSpan.FromDays(1));
        Console.WriteLine($" -> After Clear:  Status={afterClear.Status} (should be AcquiredNew — record was wiped)");

        // ─── 3. InMemoryIdempotencyStore with TimeProvider (deterministic tests) ──
        Console.WriteLine("\n3. InMemoryIdempotencyStore(TimeProvider) — deterministic time control for unit tests:");

        // FakeTimeProvider allows instant TTL simulation without actual Task.Delay
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var storeWithTime = new InMemoryIdempotencyStore(fakeTime);

        var timeKey = new IdempotencyKey("TIME-KEY-001");
        var timeFp = IdempotencyFingerprintHasher.Compute("POST", "orders", Guid.Empty.ToString(), null, []);

        // Acquire with 1-second lease
        var timeClaim1 = await storeWithTime.TryAcquireAsync(Guid.Empty, "orders", timeKey, timeFp, TimeSpan.FromSeconds(1), TimeSpan.FromDays(7));
        Console.WriteLine($" -> Acquired with 1s lease: Status={timeClaim1.Status}");

        // Advance time by 2 seconds — lease is now expired
        fakeTime.Advance(TimeSpan.FromSeconds(2));

        // Second acquisition should steal the expired lease
        var timeClaim2 = await storeWithTime.TryAcquireAsync(Guid.Empty, "orders", timeKey, timeFp, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));
        Console.WriteLine($" -> After 2s advance: Status={timeClaim2.Status} (expected AcquiredStale — lease stolen)");
        Console.WriteLine($"    IsAcquired={timeClaim2.IsAcquired}, ConcurrencyVersion={timeClaim2.ConcurrencyVersion}");

        if (timeClaim2.Status == ClaimResultStatus.AcquiredStale)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(" -> [SUCCESS] TimeProvider injection enables deterministic TTL testing!");
            Console.ResetColor();
        }

        // ─── 4. Batch processing pattern ─────────────────────────────────────────
        Console.WriteLine("\n4. Batch idempotency — processing N events each with their own unique key:");

        var batchStore = new InMemoryIdempotencyStore();
        var batchOptions = new IdempotencyOptions();
        var batchPolicy = new DefaultIdempotencyPolicy(batchOptions);
        var batchContextAccessor = new AsyncLocalIdempotencyContextAccessor();
        var batchEngine = new IdempotencyEngine(batchStore, batchPolicy, serializer, batchContextAccessor, NullLogger<IdempotencyEngine>.Instance);

        var eventIds = new[] { "EVT-001", "EVT-002", "EVT-003", "EVT-004", "EVT-005" };
        int processedCount = 0;

        foreach (var eventId in eventIds)
        {
            var evtKey = new IdempotencyKey(eventId);
            var evtFp = IdempotencyFingerprintHasher.Compute("PROCESS", "events", "tenant-A", null, Encoding.UTF8.GetBytes(eventId));

            await batchEngine.ExecuteAsync(Guid.Empty, "events", evtKey, evtFp, async ct =>
            {
                await Task.Delay(5, ct);
                processedCount++;
                return eventId;
            });
        }

        Console.WriteLine($" -> Events processed in batch: {processedCount} / {eventIds.Length}");

        // Re-running the same batch (idempotency guarantees zero re-execution)
        int reprocessedCount = 0;
        foreach (var eventId in eventIds)
        {
            var evtKey = new IdempotencyKey(eventId);
            var evtFp = IdempotencyFingerprintHasher.Compute("PROCESS", "events", "tenant-A", null, Encoding.UTF8.GetBytes(eventId));

            await batchEngine.ExecuteAsync(Guid.Empty, "events", evtKey, evtFp, async ct =>
            {
                await Task.Delay(5, ct);
                reprocessedCount++;
                return eventId;
            });
        }

        if (reprocessedCount == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($" -> [SUCCESS] Re-running same batch: 0 re-executions (all {eventIds.Length} replayed from cache)!");
            Console.ResetColor();
        }
    }

    /// <summary>
    /// Represents the sample ticket booking outcome.
    /// </summary>
    /// <param name="TicketId">The unique ticket identifier.</param>
    /// <param name="Status">The booking confirmation status.</param>
    public sealed record TicketResult(string TicketId, string Status);

    /// <summary>
    /// A minimal fake TimeProvider for deterministic time manipulation in the showcase.
    /// </summary>
    public sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        /// <summary>
        /// Initializes a new instance of the <see cref="FakeTimeProvider"/> class.
        /// </summary>
        /// <param name="initialUtcNow">The initial UTC timestamp.</param>
        public FakeTimeProvider(DateTimeOffset initialUtcNow)
        {
            _utcNow = initialUtcNow;
        }

        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow() => _utcNow;

        /// <summary>
        /// Advances the internal clock by the specified duration.
        /// </summary>
        /// <param name="duration">The duration to advance.</param>
        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
