// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using EricksonLopez.Idempotency.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace EricksonLopez.Idempotency.Benchmarks;

[MemoryDiagnoser]
public class IdempotencyPipelineBenchmarks
{
    public sealed class OrderResponse
    {
        public string OrderId { get; set; } = "ord-9999";
        public decimal Total { get; set; } = 149.99m;
        public string Status { get; set; } = "Confirmed";
    }

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _scope = "Orders";
    private readonly string _fingerprint = "a1b2c3d4e5f678901234567890abcdef1234567890abcdef1234567890abcdef";
    private readonly InMemoryIdempotencyStore _store = new();
    private readonly IdempotencyEngine _engine;
    private readonly IdempotencyKey _cachedKey = new("cached-order-key-1");
    private long _counter;

    public IdempotencyPipelineBenchmarks()
    {
        var serializer = new SystemTextJsonIdempotencySerializer();
        var options = new IdempotencyOptions();
        var policy = new DefaultIdempotencyPolicy(options);
        var accessor = new AsyncLocalIdempotencyContextAccessor();
        var logger = NullLogger<IdempotencyEngine>.Instance;

        _engine = new IdempotencyEngine(_store, policy, serializer, accessor, logger);
    }

    [GlobalSetup]
    public async Task Setup()
    {
        // Seed cached key
        await _engine.ExecuteAsync(
            _tenantId,
            _scope,
            _cachedKey,
            _fingerprint,
            ct => Task.FromResult(new OrderResponse()),
            CancellationToken.None);
    }

    [Benchmark(Baseline = true)]
    public async Task<OrderResponse> CacheHitPath()
    {
        return await _engine.ExecuteAsync(
            _tenantId,
            _scope,
            _cachedKey,
            _fingerprint,
            ct => Task.FromResult(new OrderResponse()),
            CancellationToken.None);
    }

    [Benchmark]
    public async Task<OrderResponse> CacheMissFirstExecution()
    {
        long id = Interlocked.Increment(ref _counter);
        var key = new IdempotencyKey($"new-order-key-{id}");

        return await _engine.ExecuteAsync(
            _tenantId,
            _scope,
            key,
            _fingerprint,
            ct => Task.FromResult(new OrderResponse { OrderId = $"ord-{id}" }),
            CancellationToken.None);
    }
}
