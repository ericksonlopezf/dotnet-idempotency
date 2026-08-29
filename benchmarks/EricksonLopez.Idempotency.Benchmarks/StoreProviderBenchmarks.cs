// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using EricksonLopez.Idempotency.Testing;

namespace EricksonLopez.Idempotency.Benchmarks;

[MemoryDiagnoser]
public class StoreProviderBenchmarks
{
    private static readonly Dictionary<string, string[]> SampleHeaders = new()
    {
        { "Content-Type", ["application/json"] }
    };

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _scope = "Payments";
    private readonly string _fingerprint = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private readonly byte[] _cachedPayload = Encoding.UTF8.GetBytes("{\"paymentId\":\"pay_123\",\"status\":\"succeeded\"}");
    private readonly InMemoryIdempotencyStore _store = new();
    private readonly IdempotencyKey _readKey = new("bench-read-key");
    private long _counter;

    [GlobalSetup]
    public async Task Setup()
    {
        var result = await _store.TryAcquireAsync(
            _tenantId,
            _scope,
            _readKey,
            _fingerprint,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromDays(1),
            CancellationToken.None);

        if ((result.Status == ClaimResultStatus.AcquiredNew || result.Status == ClaimResultStatus.AcquiredStale) && result.OwnerToken.HasValue)
        {
            await _store.MarkCompletedAsync(
                _tenantId,
                _scope,
                _readKey,
                result.OwnerToken.Value,
                result.ConcurrencyVersion ?? 1,
                200,
                SampleHeaders,
                _cachedPayload,
                TimeSpan.FromDays(1),
                CancellationToken.None);
        }
    }

    [Benchmark(Baseline = true)]
    public async Task<IdempotencyClaimResult> TryAcquireExistingCompletedKey()
    {
        return await _store.TryAcquireAsync(
            _tenantId,
            _scope,
            _readKey,
            _fingerprint,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromDays(1),
            CancellationToken.None);
    }

    [Benchmark]
    public async Task<bool> AcquireAndCommitNewKey()
    {
        long id = Interlocked.Increment(ref _counter);
        var key = new IdempotencyKey($"bench-acquire-{id}");

        var claim = await _store.TryAcquireAsync(
            _tenantId,
            _scope,
            key,
            _fingerprint,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromDays(1),
            CancellationToken.None);

        if ((claim.Status == ClaimResultStatus.AcquiredNew || claim.Status == ClaimResultStatus.AcquiredStale) && claim.OwnerToken.HasValue)
        {
            return await _store.MarkCompletedAsync(
                _tenantId,
                _scope,
                key,
                claim.OwnerToken.Value,
                claim.ConcurrencyVersion ?? 1,
                200,
                SampleHeaders,
                _cachedPayload,
                TimeSpan.FromDays(1),
                CancellationToken.None);
        }

        return false;
    }
}
