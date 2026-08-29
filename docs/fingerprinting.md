# Request Fingerprinting & Payload Collision Protection

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. The Key-Reuse Attack & Payload Mismatch

An idempotency key alone is insufficient to guarantee semantic safety. Consider the dangerous scenario of **Idempotency Key Reuse with Different Payloads**:

```text
Request 1: POST /payments | Key: "KEY-999" | Body: { "amount": 100, "currency": "USD" }
Request 2: POST /payments | Key: "KEY-999" | Body: { "amount": 5000, "currency": "EUR" }
```

If the server blindly replays the response of Request 1 for Request 2:
- The caller thinks their **5000 EUR** payment succeeded.
- The merchant actually charged **100 USD**.
- Financial integrity is completely compromised.

---

## 2. Deterministic Cryptographic Fingerprints

To prevent payload collisions and malicious key tampering, `EricksonLopez.Idempotency` calculates a canonical **SHA-256 Request Fingerprint**.

```text
Fingerprint = Hex(SHA-256(OperationName + ':' + Scope + ':' + TenantId + ':' + AuthenticatedSubject + ':' + PayloadBytes))
```

### Implementation Details:
`IdempotencyFingerprintHasher` uses `IncrementalHash` on .NET 10 to compute the hash with **minimal heap allocations on typical short-string operation paths** and high throughput.
For strings whose UTF-8 encoding fits in 256 bytes or fewer, the implementation uses `stackalloc` and avoids heap allocations. Longer strings (e.g., large scope identifiers) fall back to `Encoding.UTF8.GetBytes`, which allocates on the heap. `Convert.ToHexString` also allocates the result string on the heap.

```csharp
public sealed class IdempotencyFingerprintHasher : IIdempotencyFingerprintGenerator
{
    private static readonly byte[] _colonSeparator = [(byte)':'];

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
}
```

---

## 3. Fingerprint Mismatch Handling

When a claim arrives with an existing key but a conflicting fingerprint:

1. **Storage Layer**: Identifies `existing.Fingerprint != incoming.Fingerprint`.
2. **Claim Result**: Returns `ClaimResultStatus.FingerprintMismatch`.
3. **Core Engine**: Throws `IdempotencyFingerprintMismatchException`.
4. **HTTP Adapter**: Returns `409 Conflict` (RFC 9110 §15.5.10) with standardized `ProblemDetails`.
5. **Result Monad**: Maps to `Error.Validation("Idempotency.FingerprintMismatch")`.

---

## 4. Custom Fingerprint Generator (IIdempotencyFingerprintGenerator SPI)

The built-in `IdempotencyFingerprintHasher` (SHA-256 of Method + Path + TenantId + Subject + Body)
covers the vast majority of use cases. However, the `IIdempotencyFingerprintGenerator` SPI allows
you to replace it with any custom algorithm.

### When to use a custom generator

- You want to **exclude certain fields** from the fingerprint (e.g., ignore timestamps or nonces in the body).
- You need to **include additional context** not available in the standard fingerprint inputs (e.g., a `Request-Context` header).
- You require a **different hashing algorithm** (e.g., HMAC-SHA256 with a secret for server-side verification).
- You are building a **test double** that always returns a fixed fingerprint.

### Interface contract

```csharp
namespace EricksonLopez.Idempotency;

/// <summary>
/// Defines a strategy for computing deterministic cryptographic fingerprints from request components.
/// </summary>
public interface IIdempotencyFingerprintGenerator
{
    /// <summary>
    /// Computes a deterministic hexadecimal SHA-256 fingerprint from the specified operation components.
    /// </summary>
    string GenerateFingerprint(
        string operationName,
        string scope,
        string tenantId,
        string? authenticatedSubject,
        ReadOnlySpan<byte> payloadBytes);
}
```

### Example: Exclude body from fingerprint (operation + scope only)

Useful for APIs where the idempotency key alone is sufficient and payload changes are acceptable
(e.g., a GET-equivalent idempotent read operation):

```csharp
using EricksonLopez.Idempotency;
using System;
using System.Security.Cryptography;
using System.Text;

public sealed class MethodScopeFingerprintGenerator : IIdempotencyFingerprintGenerator
{
    public string GenerateFingerprint(
        string operationName,
        string scope,
        string tenantId,
        string? authenticatedSubject,
        ReadOnlySpan<byte> payloadBytes)
    {
        // Only fingerprint operation + scope + tenantId — ignore payload
        var input = $"{operationName}\n{scope}\n{tenantId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }
}
```

Registration (replaces the built-in generator):

```csharp
services.AddIdempotencyCore();
services.AddSingleton<IIdempotencyFingerprintGenerator, MethodScopeFingerprintGenerator>();
```

> [!WARNING]
> Replacing the fingerprint generator changes the collision detection behavior globally.
> Ensure the replacement generates sufficiently unique fingerprints for your use case.
> A generator that always returns the same value will suppress all fingerprint-mismatch 409 responses.

### Example: HMAC-SHA256 with a server secret

Useful for APIs that want to detect tampering of the fingerprint itself:

```csharp
public sealed class HmacFingerprintGenerator : IIdempotencyFingerprintGenerator
{
    private readonly byte[] _secretKey;

    public HmacFingerprintGenerator(string secret)
    {
        _secretKey = Encoding.UTF8.GetBytes(secret);
    }

    public string GenerateFingerprint(
        string operationName,
        string scope,
        string tenantId,
        string? authenticatedSubject,
        ReadOnlySpan<byte> payloadBytes)
    {
        using var hmac = new HMACSHA256(_secretKey);
        var prefix = Encoding.UTF8.GetBytes($"{operationName}\n{scope}\n{tenantId}\n{authenticatedSubject ?? string.Empty}\n");
        hmac.TransformBlock(prefix, 0, prefix.Length, null, 0);
        
        var body = payloadBytes.ToArray();
        hmac.TransformFinalBlock(body, 0, body.Length);
        
        return Convert.ToHexString(hmac.Hash!);
    }
}
```

