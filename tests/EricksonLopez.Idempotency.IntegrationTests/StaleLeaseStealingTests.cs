// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Idempotency.Testing;
using Xunit;

namespace EricksonLopez.Idempotency.IntegrationTests;

public sealed class StaleLeaseStealingTests
{
    [Fact]
    public async Task ExpiredLease_IsReclaimedByNewWorker_WithIncrementedConcurrencyVersion()
    {
        // Arrange
        var store = new InMemoryIdempotencyStore();
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("stale-key-1");
        var fingerprint = "fp-test";

        // 1. Initial acquisition with short 10ms lease
        var initialClaim = await store.TryAcquireAsync(
            tenantId,
            "orders",
            key,
            fingerprint,
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromDays(7));

        initialClaim.Status.Should().Be(ClaimResultStatus.AcquiredNew);
        initialClaim.ConcurrencyVersion.Should().Be(1);

        // 2. Wait for lease to expire (simulating process crash during execution)
        await Task.Delay(25);

        // 3. New request arrives after crash
        var recoveryClaim = await store.TryAcquireAsync(
            tenantId,
            "orders",
            key,
            fingerprint,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromDays(7));

        // Assert
        recoveryClaim.Status.Should().Be(ClaimResultStatus.AcquiredStale);
        recoveryClaim.IsAcquired.Should().BeTrue();
        recoveryClaim.OwnerToken.Should().NotBeNull();
        recoveryClaim.OwnerToken!.Value.Should().NotBe(initialClaim.OwnerToken!.Value);
        recoveryClaim.ConcurrencyVersion.Should().Be(2, "Concurrency version must be incremented when stealing stale lease");
    }

    [Fact]
    public async Task ZombieWorker_AttemptingToCompleteLostLease_FailsToCommit()
    {
        // Arrange
        var store = new InMemoryIdempotencyStore();
        var tenantId = Guid.NewGuid();
        var key = new IdempotencyKey("zombie-key-1");
        var fingerprint = "fp-test";

        // 1. Initial worker acquires with short lease
        var worker1Claim = await store.TryAcquireAsync(tenantId, "orders", key, fingerprint, TimeSpan.FromMilliseconds(10), TimeSpan.FromDays(7));

        // 2. Worker 1 stalls; lease expires
        await Task.Delay(25);

        // 3. Worker 2 steals the lease
        var worker2Claim = await store.TryAcquireAsync(tenantId, "orders", key, fingerprint, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));
        worker2Claim.Status.Should().Be(ClaimResultStatus.AcquiredStale);

        // 4. Worker 1 wakes up (zombie) and tries to mark completed with old OwnerToken and Version 1
        var worker1Success = await store.MarkCompletedAsync(
            tenantId,
            "orders",
            key,
            worker1Claim.OwnerToken!.Value,
            worker1Claim.ConcurrencyVersion!.Value,
            200,
            new System.Collections.Generic.Dictionary<string, string[]>(),
            ReadOnlyMemory<byte>.Empty,
            TimeSpan.FromDays(7));

        // Assert
        worker1Success.Should().BeFalse("Zombie worker must be rejected because fencing token changed");

        // 5. Worker 2 completes legitimately
        var worker2Success = await store.MarkCompletedAsync(
            tenantId,
            "orders",
            key,
            worker2Claim.OwnerToken!.Value,
            worker2Claim.ConcurrencyVersion!.Value,
            200,
            new System.Collections.Generic.Dictionary<string, string[]>(),
            ReadOnlyMemory<byte>.Empty,
            TimeSpan.FromDays(7));

        worker2Success.Should().BeTrue("Legitimate owner must be allowed to complete");
    }
}
