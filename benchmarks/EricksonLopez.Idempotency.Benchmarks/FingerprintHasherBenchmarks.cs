// Copyright © Erickson Lopez. MIT License.
using System.Text;
using BenchmarkDotNet.Attributes;

namespace EricksonLopez.Idempotency.Benchmarks;

/// <summary>
/// Provides benchmark scenarios for evaluating fingerprint hashing throughput and memory allocation profiles across payload sizes.
/// </summary>
[MemoryDiagnoser]
public class FingerprintHasherBenchmarks
{
    private byte[] _smallPayload = null!;
    private byte[] _mediumPayload = null!;
    private byte[] _largePayload = null!;

    /// <summary>
    /// Initializes benchmark payload data.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _smallPayload = Encoding.UTF8.GetBytes("{\"orderId\":\"12345\",\"amount\":99.99}");
        _mediumPayload = Encoding.UTF8.GetBytes(new string('x', 2048));
        _largePayload = Encoding.UTF8.GetBytes(new string('y', 32768));
    }

    /// <summary>
    /// Evaluates deterministic SHA-256 fingerprint generation on small payload.
    /// </summary>
    [Benchmark(Baseline = true)]
    public string ComputeFingerprintSmall()
    {
        return IdempotencyFingerprintHasher.Compute(
            "POST",
            "/api/v1/orders",
            "3fa85f64-5717-4562-b3fc-2c963f66afa6",
            "auth0|user123",
            _smallPayload);
    }

    /// <summary>
    /// Evaluates deterministic SHA-256 fingerprint generation on medium 2KB payload.
    /// </summary>
    [Benchmark]
    public string ComputeFingerprintMedium()
    {
        return IdempotencyFingerprintHasher.Compute(
            "POST",
            "/api/v1/orders",
            "3fa85f64-5717-4562-b3fc-2c963f66afa6",
            "auth0|user123",
            _mediumPayload);
    }

    /// <summary>
    /// Evaluates deterministic SHA-256 fingerprint generation on large 32KB payload.
    /// </summary>
    [Benchmark]
    public string ComputeFingerprintLarge()
    {
        return IdempotencyFingerprintHasher.Compute(
            "POST",
            "/api/v1/orders",
            "3fa85f64-5717-4562-b3fc-2c963f66afa6",
            "auth0|user123",
            _largePayload);
    }
}
