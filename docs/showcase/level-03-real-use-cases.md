# Level 03: Real Use Cases & Payload Collision Protection

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. The Key-Reuse Attack & Payload Collision

An idempotency key alone is insufficient to guarantee safety. Consider the dangerous scenario of **Idempotency Key Reuse with Different Payloads**:

```text
Request 1: POST /payments | Key: "KEY-999" | Body: { "amount": 100, "currency": "USD" }
Request 2: POST /payments | Key: "KEY-999" | Body: { "amount": 5000, "currency": "EUR" }
```

If the server blindly replays the response of Request 1 for Request 2:
- The caller thinks their **5,000 EUR** payment succeeded.
- The merchant actually charged **100 USD**.
- Financial integrity is completely compromised!

---

## 2. SHA-256 Fingerprint Protection

To prevent payload collisions and malicious key tampering, `EricksonLopez.Idempotency` calculates a canonical **SHA-256 Request Fingerprint**:

$$\text{Fingerprint} = \text{Hex}(\text{SHA256}(\text{Method} \parallel \text{Scope} \parallel \text{TenantId} \parallel \text{Subject} \parallel \text{PayloadBytes}))$$

When a request arrives with an existing key:
1. If the stored fingerprint matches the incoming fingerprint $\rightarrow$ **Safe Replay (`CompletedReplay`)**.
2. If the stored fingerprint does NOT match $\rightarrow$ **Security Breach Detection (`ClaimResultStatus.FingerprintMismatch`)**.
3. The engine immediately throws `IdempotencyFingerprintMismatchException`, and the HTTP layer returns **RFC 9110 HTTP 409 Conflict** (or 400 ProblemDetails).

---

## 3. Code Walkthrough

```csharp
using System;
using System.Text;
using System.Threading.Tasks;
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.Exceptions;
using EricksonLopez.Idempotency.Testing;
using Microsoft.Extensions.Logging.Abstractions;

var store = new InMemoryIdempotencyStore();
var options = new IdempotencyOptions();
var policy = new DefaultIdempotencyPolicy(options);
var serializer = new SystemTextJsonIdempotencySerializer();
var contextAccessor = new AsyncLocalIdempotencyContextAccessor();
var engine = new IdempotencyEngine(store, policy, serializer, contextAccessor, NullLogger<IdempotencyEngine>.Instance);

var idempotencyKey = new IdempotencyKey("PAYMENT-TX-001");
var tenantId = Guid.NewGuid();

// 1. Initial legitimate payment of $100.00
var legitimatePayload = Encoding.UTF8.GetBytes("{\"accountId\":\"ACC-99\",\"amount\":100.00,\"currency\":\"USD\"}");
var legitimateFp = IdempotencyFingerprintHasher.Compute("POST", "/payments", tenantId.ToString(), null, legitimatePayload);

var result1 = await engine.ExecuteAsync(tenantId, "payments", idempotencyKey, legitimateFp, async ct =>
{
    await Task.Delay(10, ct);
    return new PaymentResult("TX-100", 100.00m, "Authorized");
});

Console.WriteLine($"Legitimate: Result={result1.Status}, AuthCode={result1.TransactionId}");

// 2. Tampered payment attempt ($5,000.00 reusing PAYMENT-TX-001)
var tamperedPayload = Encoding.UTF8.GetBytes("{\"accountId\":\"ACC-99\",\"amount\":5000.00,\"currency\":\"USD\"}");
var tamperedFp = IdempotencyFingerprintHasher.Compute("POST", "/payments", tenantId.ToString(), null, tamperedPayload);

try
{
    await engine.ExecuteAsync(tenantId, "payments", idempotencyKey, tamperedFp, async ct =>
    {
        await Task.Delay(10, ct);
        return new PaymentResult("TX-5000", 5000.00m, "Authorized");
    });
}
catch (IdempotencyFingerprintMismatchException ex)
{
    Console.WriteLine($"[BLOCKED] IdempotencyFingerprintMismatchException: {ex.Message}");
}

public sealed record PaymentResult(string TransactionId, decimal Amount, string Status);
```

---

## 4. Next Steps

Proceed to [Level 04: Advanced Integration (Result & Mediator & Outbox)](level-04-advanced-integration.md).
