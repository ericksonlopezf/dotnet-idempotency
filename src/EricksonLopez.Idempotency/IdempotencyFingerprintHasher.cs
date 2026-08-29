// Copyright © Erickson Lopez. MIT License.
using System;
using System.Security.Cryptography;
using System.Text;

namespace EricksonLopez.Idempotency;

/// <summary>
/// Provides high-performance, deterministic SHA-256 cryptographic fingerprint generation for idempotent operations.
/// </summary>
public sealed class IdempotencyFingerprintHasher : IIdempotencyFingerprintGenerator
{
    private static readonly byte[] _colonSeparator = [(byte)':'];

    /// <summary>
    /// Computes a deterministic hexadecimal SHA-256 fingerprint from the canonical components of an operation.
    /// </summary>
    /// <param name="operationName">The logical operation or endpoint route name.</param>
    /// <param name="scope">The functional partition scope.</param>
    /// <param name="tenantId">The string tenant identifier.</param>
    /// <param name="authenticatedSubject">The authenticated subject or user identifier if present; otherwise, <see langword="null"/>.</param>
    /// <param name="payloadBytes">The raw payload byte content to hash.</param>
    /// <returns>An uppercase hexadecimal SHA-256 hash string uniquely identifying the combination of all provided inputs.</returns>
    public static string Compute(
        string operationName,
        string scope,
        string tenantId,
        string? authenticatedSubject,
        ReadOnlySpan<byte> payloadBytes)
    {
        using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        Span<byte> tempBuffer = stackalloc byte[256];

        AppendUtf8String(incrementalHash, operationName, tempBuffer);
        incrementalHash.AppendData(_colonSeparator);

        AppendUtf8String(incrementalHash, scope, tempBuffer);
        incrementalHash.AppendData(_colonSeparator);

        AppendUtf8String(incrementalHash, tenantId, tempBuffer);
        incrementalHash.AppendData(_colonSeparator);

        if (!string.IsNullOrEmpty(authenticatedSubject))
        {
            AppendUtf8String(incrementalHash, authenticatedSubject, tempBuffer);
        }
        incrementalHash.AppendData(_colonSeparator);

        if (!payloadBytes.IsEmpty)
        {
            incrementalHash.AppendData(payloadBytes);
        }

        Span<byte> hashOutput = stackalloc byte[32];
        incrementalHash.GetHashAndReset(hashOutput);

        return Convert.ToHexString(hashOutput);
    }

    /// <inheritdoc />
    public string GenerateFingerprint(
        string operationName,
        string scope,
        string tenantId,
        string? authenticatedSubject,
        ReadOnlySpan<byte> payloadBytes)
    {
        return Compute(operationName, scope, tenantId, authenticatedSubject, payloadBytes);
    }

    private static void AppendUtf8String(IncrementalHash hash, string value, Span<byte> tempBuffer)
    {
        var maxBytes = Encoding.UTF8.GetMaxByteCount(value.Length);
        if (maxBytes <= tempBuffer.Length)
        {
            var written = Encoding.UTF8.GetBytes(value, tempBuffer);
            hash.AppendData(tempBuffer[..written]);
        }
        else
        {
            var heapBuffer = Encoding.UTF8.GetBytes(value);
            hash.AppendData(heapBuffer);
        }
    }
}
