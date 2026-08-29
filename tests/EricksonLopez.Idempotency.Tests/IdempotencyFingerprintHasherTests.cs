// Copyright © Erickson Lopez. MIT License.
using System;
using System.Text;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.Idempotency.Tests;

public sealed class IdempotencyFingerprintHasherTests
{
    private readonly IdempotencyFingerprintHasher _generator = new();

    [Fact]
    public void Compute_WithStandardInputs_MatchesExactGoldenHash()
    {
        var body = Encoding.UTF8.GetBytes("{\"amount\": 100}");
        var hash = IdempotencyFingerprintHasher.Compute("POST", "/payments", "tenant-1", "user-1", body);

        // Deterministic golden hash verification ensuring no separator, parameter or encoding mutation survives
        var expected = IdempotencyFingerprintHasher.Compute("POST", "/payments", "tenant-1", "user-1", body);
        hash.Should().Be(expected);
        hash.Should().Be("9859C8B789EA00943DAF358807DF68D3970C80718316B04695E73DE5DFD89E66");
    }

    [Fact]
    public void Compute_WithNullSubject_MatchesExactGoldenHash()
    {
        var body = Encoding.UTF8.GetBytes("{\"amount\": 100}");
        var hash = IdempotencyFingerprintHasher.Compute("POST", "/payments", "tenant-1", null, body);

        hash.Should().Be("CCEA572EDF804C6A24E4FFA29BA7233969F2454E00A6F2B767EB78456E30F1ED");
    }

    [Fact]
    public void Compute_WithEmptyPayload_MatchesExactGoldenHash()
    {
        var hash = IdempotencyFingerprintHasher.Compute("GET", "status", "tenant-1", null, ReadOnlySpan<byte>.Empty);

        hash.Should().Be("A565F58BAE6BB8EFC959A6C729289724F1D5E88A2ACF43889DE88E914B77DFFB");
    }

    [Fact]
    public void Compute_WithLongInputs_ExercisesHeapBuffer_MatchesExactGoldenHash()
    {
        var longOp = new string('O', 300);
        var longScope = new string('S', 300);
        var longTenant = new string('T', 300);
        var longSubject = new string('U', 300);
        var body = Encoding.UTF8.GetBytes(new string('B', 1000));

        var hash = IdempotencyFingerprintHasher.Compute(longOp, longScope, longTenant, longSubject, body);
        var instanceHash = _generator.GenerateFingerprint(longOp, longScope, longTenant, longSubject, body);

        hash.Should().Be(instanceHash);
        hash.Length.Should().Be(64);
    }

    [Fact]
    public void Compute_WithExactStackallocBoundary_BehavesDeterministically()
    {
        // 256 bytes UTF-8 boundary (85 * 3 = 255 max bytes vs 86 * 3 = 258 max bytes)
        var exactOp = new string('X', 85);
        var exactScope = new string('Y', 85);
        var exactTenant = new string('Z', 85);
        var exactSubject = new string('W', 85);

        var hash = IdempotencyFingerprintHasher.Compute(exactOp, exactScope, exactTenant, exactSubject, ReadOnlySpan<byte>.Empty);
        hash.Should().Be("9E9F39AE7B5047BC20268A8AC4C427C9E7442FB09FAAD96A373BAB9BD218BE6F");

        var heapOp = new string('X', 86);
        var heapHash = IdempotencyFingerprintHasher.Compute(heapOp, exactScope, exactTenant, exactSubject, ReadOnlySpan<byte>.Empty);
        heapHash.Length.Should().Be(64);
        heapHash.Should().NotBe(hash);
    }
}
