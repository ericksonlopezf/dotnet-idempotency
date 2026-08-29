// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Idempotency.Testing;
using Xunit;

namespace EricksonLopez.Idempotency.IntegrationTests;

public sealed class ConcurrencyRaceTests
{
    [Fact]
    public async Task ConcurrentAcquire_With100ParallelRequests_ExactlyOneAcquiresNew()
    {
        // Arrange
        var store = new InMemoryIdempotencyStore();
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("race-key-100");
        var fingerprint = "canonical-fp-test";

        var claimResults = new ConcurrentBag<IdempotencyClaimResult>();
        using var barrier = new Barrier(100);

        // Act
        var tasks = Enumerable.Range(0, 100).Select(async _ =>
        {
            await Task.Yield();
            barrier.SignalAndWait();
            var claim = await store.TryAcquireAsync(
                tenantId,
                "scope-race",
                key,
                fingerprint,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromDays(7));
            claimResults.Add(claim);
        });

        await Task.WhenAll(tasks);

        // Assert
        var acquiredCount = claimResults.Count(r => r.Status == ClaimResultStatus.AcquiredNew);
        var inFlightCount = claimResults.Count(r => r.Status == ClaimResultStatus.InFlightConflict);

        acquiredCount.Should().Be(1, "Exactly one thread must win the race and acquire the new key");
        inFlightCount.Should().Be(99, "All competing concurrent threads must receive in-flight conflict");
    }

    [Fact]
    public async Task ConcurrentReplay_AfterCompletion_All100ParallelRequestsReceiveCachedResponse()
    {
        // Arrange
        var store = new InMemoryIdempotencyStore();
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("replay-race-100");
        var fingerprint = "canonical-fp-test";

        var initialClaim = await store.TryAcquireAsync(tenantId, "scope-race", key, fingerprint, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));
        await store.MarkCompletedAsync(
            tenantId,
            "scope-race",
            key,
            initialClaim.OwnerToken!.Value,
            initialClaim.ConcurrencyVersion!.Value,
            201,
            new System.Collections.Generic.Dictionary<string, string[]>(),
            new byte[] { 42 },
            TimeSpan.FromDays(7));

        var claimResults = new ConcurrentBag<IdempotencyClaimResult>();
        using var barrier = new Barrier(100);

        // Act
        var tasks = Enumerable.Range(0, 100).Select(async _ =>
        {
            await Task.Yield();
            barrier.SignalAndWait();
            var claim = await store.TryAcquireAsync(
                tenantId,
                "scope-race",
                key,
                fingerprint,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromDays(7));
            claimResults.Add(claim);
        });

        await Task.WhenAll(tasks);

        // Assert
        claimResults.Count.Should().Be(100);
        claimResults.All(r => r.Status == ClaimResultStatus.CompletedReplay).Should().BeTrue();
        claimResults.All(r => r.CachedResponse!.StatusCode == 201).Should().BeTrue();
    }
}
