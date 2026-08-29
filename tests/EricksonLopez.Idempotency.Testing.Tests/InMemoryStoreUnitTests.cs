// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Idempotency.Testing;
using Xunit;

namespace EricksonLopez.Idempotency.Testing.Tests;

public sealed class InMemoryStoreUnitTests
{
    [Fact]
    public async Task TryAcquireAsync_NewKey_AcquiresSuccessfully()
    {
        var store = new InMemoryIdempotencyStore();
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("inmem-key-1");

        var claim = await store.TryAcquireAsync(tenantId, "test", key, "fp-1", TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));

        claim.Status.Should().Be(ClaimResultStatus.AcquiredNew);
        claim.OwnerToken.Should().NotBeNull();
        claim.ConcurrencyVersion.Should().Be(1);
        claim.CachedResponse.Should().BeNull();
        claim.ExistingFingerprint.Should().BeNull();
        claim.IsAcquired.Should().BeTrue();
        claim.IsReplay.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireAsync_FingerprintMismatch_ReturnsMismatchStatusAndExistingFingerprint()
    {
        var store = new InMemoryIdempotencyStore();
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("inmem-mismatch");

        await store.TryAcquireAsync(tenantId, "orders", key, "fp-original", TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));

        var claimMismatch = await store.TryAcquireAsync(tenantId, "orders", key, "fp-tampered", TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));

        claimMismatch.Status.Should().Be(ClaimResultStatus.FingerprintMismatch);
        claimMismatch.ExistingFingerprint.Should().Be("fp-original");
        claimMismatch.OwnerToken.Should().BeNull();
        claimMismatch.ConcurrencyVersion.Should().BeNull();
        claimMismatch.CachedResponse.Should().BeNull();
        claimMismatch.IsAcquired.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireAsync_InFlightProcessing_ReturnsInFlightConflict()
    {
        var store = new InMemoryIdempotencyStore();
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("inmem-conflict");

        await store.TryAcquireAsync(tenantId, "orders", key, "fp-1", TimeSpan.FromMinutes(5), TimeSpan.FromDays(7));

        var claimConflict = await store.TryAcquireAsync(tenantId, "orders", key, "fp-1", TimeSpan.FromMinutes(5), TimeSpan.FromDays(7));

        claimConflict.Status.Should().Be(ClaimResultStatus.InFlightConflict);
        claimConflict.ExistingFingerprint.Should().Be("fp-1");
        claimConflict.OwnerToken.Should().BeNull();
        claimConflict.ConcurrencyVersion.Should().BeNull();
        claimConflict.CachedResponse.Should().BeNull();
        claimConflict.IsAcquired.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenLeaseExpires_AcquiresStaleWithIncrementedVersion()
    {
        var store = new InMemoryIdempotencyStore();
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("inmem-stale");

        var firstClaim = await store.TryAcquireAsync(tenantId, "orders", key, "fp-1", TimeSpan.FromMilliseconds(15), TimeSpan.FromDays(7));
        firstClaim.Status.Should().Be(ClaimResultStatus.AcquiredNew);
        firstClaim.ConcurrencyVersion.Should().Be(1);

        await Task.Delay(35); // Wait for lease to expire

        var secondClaim = await store.TryAcquireAsync(tenantId, "orders", key, "fp-1", TimeSpan.FromMinutes(5), TimeSpan.FromDays(7));
        secondClaim.Status.Should().Be(ClaimResultStatus.AcquiredStale);
        secondClaim.OwnerToken.Should().NotBeNull();
        secondClaim.OwnerToken!.Value.Should().NotBe(firstClaim.OwnerToken!.Value);
        secondClaim.ConcurrencyVersion.Should().Be(2);
        secondClaim.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenRecordIsFailed_AllowsRetryAndAcquiresStale()
    {
        var store = new InMemoryIdempotencyStore();
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("inmem-failed-retry");

        var firstClaim = await store.TryAcquireAsync(tenantId, "orders", key, "fp-1", TimeSpan.FromMinutes(5), TimeSpan.FromDays(7));
        await store.MarkFailedAsync(tenantId, "orders", key, firstClaim.OwnerToken!.Value, firstClaim.ConcurrencyVersion!.Value);

        var secondClaim = await store.TryAcquireAsync(tenantId, "orders", key, "fp-1", TimeSpan.FromMinutes(5), TimeSpan.FromDays(7));
        secondClaim.Status.Should().Be(ClaimResultStatus.AcquiredStale);
        secondClaim.OwnerToken.Should().NotBeNull();
        secondClaim.ConcurrencyVersion.Should().Be(2);
        secondClaim.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task MarkCompletedAsync_AndReplay_ReturnsCachedResponseWithExactHeadersAndBody()
    {
        var store = new InMemoryIdempotencyStore();
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("inmem-complete-replay");

        var claim = await store.TryAcquireAsync(tenantId, "orders", key, "fp-1", TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));

        var headers = new Dictionary<string, string[]>
        {
            ["X-Custom-Header"] = new[] { "HeaderValue1", "HeaderValue2" }
        };
        var body = new byte[] { 10, 20, 30, 40 };

        var success = await store.MarkCompletedAsync(
            tenantId,
            "orders",
            key,
            claim.OwnerToken!.Value,
            claim.ConcurrencyVersion!.Value,
            201,
            headers,
            body,
            TimeSpan.FromDays(7));

        success.Should().BeTrue();

        var replay = await store.TryAcquireAsync(tenantId, "orders", key, "fp-1", TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));
        replay.Status.Should().Be(ClaimResultStatus.CompletedReplay);
        replay.IsReplay.Should().BeTrue();
        replay.ExistingFingerprint.Should().Be("fp-1");
        replay.CachedResponse.Should().NotBeNull();
        replay.CachedResponse!.StatusCode.Should().Be(201);
        replay.CachedResponse.Headers["X-Custom-Header"].Should().Contain("HeaderValue1");
        replay.CachedResponse.Body.ToArray().Should().Equal(body);
    }

    [Fact]
    public async Task MarkCompletedAsync_WithInvalidTokenOrVersionOrNonExistentKey_ReturnsFalse()
    {
        var store = new InMemoryIdempotencyStore();
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("inmem-complete-invalid");

        var claim = await store.TryAcquireAsync(tenantId, "orders", key, "fp-1", TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));

        // 1. Wrong Owner Token
        var wrongToken = await store.MarkCompletedAsync(
            tenantId, "orders", key, Guid.NewGuid(), claim.ConcurrencyVersion!.Value, 200, new Dictionary<string, string[]>(), ReadOnlyMemory<byte>.Empty, TimeSpan.FromDays(7));
        wrongToken.Should().BeFalse();

        // 2. Wrong Concurrency Version
        var wrongVersion = await store.MarkCompletedAsync(
            tenantId, "orders", key, claim.OwnerToken!.Value, 999, 200, new Dictionary<string, string[]>(), ReadOnlyMemory<byte>.Empty, TimeSpan.FromDays(7));
        wrongVersion.Should().BeFalse();

        // 3. Non-existent Key
        var nonExistent = await store.MarkCompletedAsync(
            tenantId, "orders", new IdempotencyKey("non-existent"), claim.OwnerToken!.Value, claim.ConcurrencyVersion!.Value, 200, new Dictionary<string, string[]>(), ReadOnlyMemory<byte>.Empty, TimeSpan.FromDays(7));
        nonExistent.Should().BeFalse();
    }

    [Fact]
    public async Task MarkFailedAsync_WithValidAndInvalidInputs_BehavesCorrectly()
    {
        var store = new InMemoryIdempotencyStore();
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("inmem-fail");

        var claim = await store.TryAcquireAsync(tenantId, "orders", key, "fp-1", TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));

        // 1. Wrong Owner Token
        var wrongToken = await store.MarkFailedAsync(tenantId, "orders", key, Guid.NewGuid(), claim.ConcurrencyVersion!.Value);
        wrongToken.Should().BeFalse();

        // 2. Wrong Concurrency Version
        var wrongVersion = await store.MarkFailedAsync(tenantId, "orders", key, claim.OwnerToken!.Value, 999);
        wrongVersion.Should().BeFalse();

        // 3. Non-existent Key
        var nonExistent = await store.MarkFailedAsync(tenantId, "orders", new IdempotencyKey("non-existent"), claim.OwnerToken!.Value, claim.ConcurrencyVersion!.Value);
        nonExistent.Should().BeFalse();

        // 4. Successful MarkFailed
        var success = await store.MarkFailedAsync(tenantId, "orders", key, claim.OwnerToken!.Value, claim.ConcurrencyVersion!.Value);
        success.Should().BeTrue();
    }

    [Fact]
    public async Task CleanupExpiredRecordsAsync_RespectsBatchSizeAndOnlyPurgesExpired()
    {
        var store = new InMemoryIdempotencyStore();
        var tenantId = Guid.NewGuid();

        // Create 3 expired records
        for (var i = 1; i <= 3; i++)
        {
            var key = new IdempotencyKey($"exp-{i}");
            var claim = await store.TryAcquireAsync(tenantId, "test", key, "fp-1", TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(5));
            await store.MarkCompletedAsync(tenantId, "test", key, claim.OwnerToken!.Value, claim.ConcurrencyVersion!.Value, 200, new Dictionary<string, string[]>(), ReadOnlyMemory<byte>.Empty, TimeSpan.FromMilliseconds(5));
        }

        // Create 1 active non-expired record
        var activeKey = new IdempotencyKey("active-1");
        var activeClaim = await store.TryAcquireAsync(tenantId, "test", activeKey, "fp-1", TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        await store.MarkCompletedAsync(tenantId, "test", activeKey, activeClaim.OwnerToken!.Value, activeClaim.ConcurrencyVersion!.Value, 200, new Dictionary<string, string[]>(), ReadOnlyMemory<byte>.Empty, TimeSpan.FromHours(1));

        await Task.Delay(25); // Wait for expiration of the 3 records

        // Purge with batchSize = 2 (should only purge 2)
        var purgedBatch = await store.CleanupExpiredRecordsAsync(DateTimeOffset.UtcNow, 2);
        purgedBatch.Should().Be(2);

        // Purge remaining expired (should purge 1)
        var purgedRemaining = await store.CleanupExpiredRecordsAsync(DateTimeOffset.UtcNow, 10);
        purgedRemaining.Should().Be(1);

        // Active record should still exist
        var activeReplay = await store.TryAcquireAsync(tenantId, "test", activeKey, "fp-1", TimeSpan.FromHours(1), TimeSpan.FromHours(1));
        activeReplay.Status.Should().Be(ClaimResultStatus.CompletedReplay);
    }

    [Fact]
    public async Task Clear_RemovesAllEntries()
    {
        var store = new InMemoryIdempotencyStore();
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("inmem-clear");

        await store.TryAcquireAsync(tenantId, "test", key, "fp-1", TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));
        store.Clear();

        var claim = await store.TryAcquireAsync(tenantId, "test", key, "fp-1", TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));
        claim.Status.Should().Be(ClaimResultStatus.AcquiredNew);
    }

    [Fact]
    public async Task MarkCompletedAsync_WithNullHeaders_ReplaysWithEmptyDefaults()
    {
        var store = new InMemoryIdempotencyStore();
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("inmem-null-defaults");

        var claim = await store.TryAcquireAsync(tenantId, "orders", key, "fp-1", TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));
        var success = await store.MarkCompletedAsync(
            tenantId,
            "orders",
            key,
            claim.OwnerToken!.Value,
            claim.ConcurrencyVersion!.Value,
            200,
            null!,
            ReadOnlyMemory<byte>.Empty,
            TimeSpan.FromDays(7));

        success.Should().BeTrue();

        var replay = await store.TryAcquireAsync(tenantId, "orders", key, "fp-1", TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));
        replay.Status.Should().Be(ClaimResultStatus.CompletedReplay);
        replay.CachedResponse.Should().NotBeNull();
        replay.CachedResponse!.Headers.Should().NotBeNull();
        replay.CachedResponse.Headers.Should().BeEmpty();
        replay.CachedResponse.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task TryAcquireAsync_ConcurrentAcquisition_ExactlyOneAcquiresAndOthersConflict()
    {
        var store = new InMemoryIdempotencyStore();
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("inmem-race-key");

        using var barrier = new Barrier(60);
        var tasks = new List<Task<IdempotencyClaimResult>>();
        for (var i = 0; i < 60; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                barrier.SignalAndWait();
                return store.TryAcquireAsync(tenantId, "orders", key, "fp-race", TimeSpan.FromMinutes(5), TimeSpan.FromDays(7));
            }));
        }

        var results = await Task.WhenAll(tasks);

        results.Should().ContainSingle(r => r.Status == ClaimResultStatus.AcquiredNew);
        results.Where(r => r.Status == ClaimResultStatus.InFlightConflict).Should().HaveCount(59);
    }

    [Fact]
    public async Task TryAcquireAsync_ConcurrentStaleSteal_ExactlyOneStealsAndOthersConflict()
    {
        var store = new InMemoryIdempotencyStore();
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("inmem-stale-race");

        // Seed with expired record
        var seedClaim = await store.TryAcquireAsync(tenantId, "orders", key, "fp-stale", TimeSpan.FromMilliseconds(10), TimeSpan.FromDays(7));
        seedClaim.Status.Should().Be(ClaimResultStatus.AcquiredNew);
        await Task.Delay(30); // Expire lease

        using var barrier = new Barrier(60);
        var tasks = new List<Task<IdempotencyClaimResult>>();
        for (var i = 0; i < 60; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                barrier.SignalAndWait();
                return store.TryAcquireAsync(tenantId, "orders", key, "fp-stale", TimeSpan.FromMinutes(5), TimeSpan.FromDays(7));
            }));
        }

        var results = await Task.WhenAll(tasks);

        results.Should().ContainSingle(r => r.Status == ClaimResultStatus.AcquiredStale);
        results.Where(r => r.Status == ClaimResultStatus.InFlightConflict).Should().HaveCount(59);
    }

    [Fact]
    public void TryAcquireAsync_HighParallelismNewKey_ExercisesConcurrentTryAdd()
    {
        for (var run = 0; run < 20; run++)
        {
            var store = new InMemoryIdempotencyStore();
            var tenantId = Guid.NewGuid();
            var key = new IdempotencyKey($"inmem-high-parallel-{run}");

            var results = new ConcurrentBag<IdempotencyClaimResult>();
            Parallel.For(0, 50, _ =>
            {
                var claim = store.TryAcquireAsync(tenantId, "orders", key, "fp-high", TimeSpan.FromMinutes(5), TimeSpan.FromDays(7)).GetAwaiter().GetResult();
                results.Add(claim);
            });

            results.Should().ContainSingle(r => r.Status == ClaimResultStatus.AcquiredNew);
            results.Where(r => r.Status == ClaimResultStatus.InFlightConflict).Should().HaveCount(49);
        }
    }

    [Fact]
    public async Task TryAcquireAsync_ActiveLeaseBoundary_ReturnsInFlightConflict()
    {
        var fixedTime = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new TestTimeProvider(fixedTime);
        var store = new InMemoryIdempotencyStore(timeProvider);
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("inmem-lease-boundary");

        // Acquire with TimeSpan.Zero lease duration (so LeaseExpiresAtUtc == fixedTime)
        var claim1 = await store.TryAcquireAsync(tenantId, "orders", key, "fp-1", TimeSpan.Zero, TimeSpan.FromDays(7));
        claim1.Status.Should().Be(ClaimResultStatus.AcquiredNew);

        // Immediate second acquire at the exact same fixedTime instant (LeaseExpiresAtUtc >= now is TRUE)
        var claim2 = await store.TryAcquireAsync(tenantId, "orders", key, "fp-1", TimeSpan.Zero, TimeSpan.FromDays(7));
        claim2.Status.Should().Be(ClaimResultStatus.InFlightConflict);
    }

    [Fact]
    public async Task CleanupExpiredRecordsAsync_ExactRetentionBoundary_PurgesOnlyStrictlyExpired()
    {
        var startTime = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new TestTimeProvider(startTime);
        var store = new InMemoryIdempotencyStore(timeProvider);
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("inmem-retention-boundary");

        var claim = await store.TryAcquireAsync(tenantId, "test", key, "fp-1", TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10));
        await store.MarkCompletedAsync(tenantId, "test", key, claim.OwnerToken!.Value, claim.ConcurrencyVersion!.Value, 200, new Dictionary<string, string[]>(), ReadOnlyMemory<byte>.Empty, TimeSpan.FromMinutes(10));

        var exactExpiryTime = startTime.AddMinutes(10);

        // Exactly at exactExpiryTime (RetentionExpiresAtUtc < utcNow is FALSE since equal): nothing purged
        var purgedExact = await store.CleanupExpiredRecordsAsync(exactExpiryTime, 100);
        purgedExact.Should().Be(0);

        // Advance 1 tick past retention expiry (RetentionExpiresAtUtc < utcNow is TRUE): record is purged
        var afterExpiryTime = exactExpiryTime.AddTicks(1);
        var purgedAfter = await store.CleanupExpiredRecordsAsync(afterExpiryTime, 100);
        purgedAfter.Should().Be(1);
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        public TestTimeProvider(DateTimeOffset initialTime) => _utcNow = initialTime;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void SetUtcNow(DateTimeOffset time) => _utcNow = time;
    }
}
