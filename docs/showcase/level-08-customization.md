# Level 08: Customization & Extension Points

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Overview

`EricksonLopez.Idempotency` is built around clean Service Provider Interfaces (SPIs) that allow complete customization without modifying framework internals:
- `IIdempotencyPolicy`: Custom lease TTLs, retention policies, and cacheability status codes.
- `IIdempotencySerializer`: Custom serialization engines (e.g. MessagePack, Protobuf, custom JSON).
- `IIdempotencyFingerprintGenerator`: Custom cryptographic request hashing strategies.

---

## 2. Custom Policy Implementation

```csharp
using System;
using EricksonLopez.Idempotency;

public sealed class StrictPaymentPolicy : IIdempotencyPolicy
{
    // Custom 2-minute in-flight lease
    public TimeSpan LeaseDuration => TimeSpan.FromMinutes(2);

    // Custom 30-day completed retention
    public TimeSpan RetentionDuration => TimeSpan.FromDays(30);

    // Disallow immediate retries on failed payments
    public bool AllowRetryOnFailure => false;

    // Only cache 200 OK and 201 Created
    public bool IsCacheableStatusCode(int statusCode) => statusCode is 200 or 201;
}
```

---

## 3. Custom Serializer Implementation

```csharp
using System;
using System.Text.Json;
using EricksonLopez.Idempotency;

public sealed class PrettyJsonSerializer : IIdempotencySerializer
{
    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, _options);

    public T? Deserialize<T>(ReadOnlyMemory<byte> bytes) => JsonSerializer.Deserialize<T>(bytes.Span, _options);
}
```

---

## 4. Custom Fingerprint Generator Implementation

```csharp
using System;
using System.Security.Cryptography;
using System.Text;
using EricksonLopez.Idempotency;

public sealed class MethodScopeFingerprintGenerator : IIdempotencyFingerprintGenerator
{
    public string GenerateFingerprint(
        string operationName,
        string scope,
        string tenantId,
        string? authenticatedSubject,
        ReadOnlySpan<byte> payloadBytes)
    {
        // Custom strategy: Hash only operation + scope + tenant
        var raw = $"{operationName}:{scope}:{tenantId}:{authenticatedSubject ?? string.Empty}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }
}
```

---

## 5. Dependency Injection Registration

```csharp
services.AddIdempotencyCore();
services.AddSingleton<IIdempotencyPolicy, StrictPaymentPolicy>();
services.AddSingleton<IIdempotencySerializer, PrettyJsonSerializer>();
services.AddSingleton<IIdempotencyFingerprintGenerator, MethodScopeFingerprintGenerator>();
```

---

## 6. Next Steps

Proceed to [Level 09: Multi-Database Persistence Adapters](level-09-persistence-extensions.md).
