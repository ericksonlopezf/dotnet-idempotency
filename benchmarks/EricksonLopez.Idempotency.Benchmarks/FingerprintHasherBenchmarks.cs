// Copyright © Erickson Lopez. MIT License.
using System.Text;
using BenchmarkDotNet.Attributes;

namespace EricksonLopez.Idempotency.Benchmarks;

/// <summary>
/// Provides benchmark scenarios for evaluating fingerprint hashing throughput and memory allocation profiles.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class FingerprintHasherBenchmarks
{
    private byte[] _payload = null!;

    /// <summary>
    /// Initializes benchmark payload data.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _payload = Encoding.UTF8.GetBytes("{\"orderId\":\"12345\",\"amount\":999.99,\"currency\":\"USD\",\"items\":[{\"sku\":\"SKU-1\",\"qty\":2}]}");
    }

    /// <summary>
    /// Evaluates deterministic SHA-256 fingerprint generation performance.
    /// </summary>
    /// <returns>The computed hexadecimal fingerprint string.</returns>
    [Benchmark]
    public string ComputeFingerprint()
    {
        return IdempotencyFingerprintHasher.Compute(
            "POST",
            "/api/v1/orders",
            "3fa85f64-5717-4562-b3fc-2c963f66afa6",
            "auth0|user123",
            _payload);
    }
}
