# Level 01: Quick Start & Primitives

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Overview

Level 01 demonstrates the core primitives of `EricksonLopez.Idempotency` and the minimal code required to execute an operation with guaranteed exactly-once semantics.

---

## 2. Core Primitives

### `IdempotencyKey`
A strongly-typed, immutable Value Object encapsulating the uniqueness key (1 to 128 characters):
```csharp
var key = new IdempotencyKey("REQ-ORD-98765");
```

### `IdempotencyScope`
A functional boundary partition that isolates keys by operation category (e.g. `orders`, `payments`, `shipments`):
```csharp
var scope = new IdempotencyScope("orders");
```

### `IdempotencyFingerprintHasher`
Zero-allocation canonical SHA-256 cryptographic digest generator for payload collision protection:
```csharp
var fingerprint = IdempotencyFingerprintHasher.Compute(
    operationName: "POST",
    scope: "/api/v1/orders",
    tenantId: "tenant-1",
    authenticatedSubject: "user-1",
    payloadBytes: Encoding.UTF8.GetBytes("{\"item\":\"Book\",\"price\":29.99}"));
```

---

## 3. Minimal Execution Example

```csharp
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.Testing;
using Microsoft.Extensions.Logging.Abstractions;

// 1. Setup in-memory engine
var store = new InMemoryIdempotencyStore();
var options = new IdempotencyOptions();
var policy = new DefaultIdempotencyPolicy(options);
var serializer = new SystemTextJsonIdempotencySerializer();
var contextAccessor = new AsyncLocalIdempotencyContextAccessor();
var engine = new IdempotencyEngine(store, policy, serializer, contextAccessor, NullLogger<IdempotencyEngine>.Instance);

// 2. Define key and request parameters
var key = new IdempotencyKey("REQ-ORD-98765");
var scope = new IdempotencyScope("orders");
var fingerprint = IdempotencyFingerprintHasher.Compute("POST", "/api/v1/orders", "tenant-1", "user-1", Encoding.UTF8.GetBytes("{\"item\":\"Book\",\"price\":29.99}"));

int executionCount = 0;

async Task<OrderResult> CreateOrderAsync(CancellationToken ct)
{
    await Task.Delay(50, ct);
    executionCount++;
    return new OrderResult("ORD-98765", "Created", 29.99m, DateTimeOffset.UtcNow);
}

// 3. First invocation — executes business logic
var firstResult = await engine.ExecuteAsync(Guid.Empty, scope.Value, key, fingerprint, CreateOrderAsync);
Console.WriteLine($"Call 1: OrderId={firstResult.OrderId}, Executions={executionCount}"); // Executions = 1

// 4. Second invocation with same key (Network Retry) — returns cached replay
var secondResult = await engine.ExecuteAsync(Guid.Empty, scope.Value, key, fingerprint, CreateOrderAsync);
Console.WriteLine($"Call 2: OrderId={secondResult.OrderId}, Executions={executionCount}"); // Executions = 1 (Handler NOT re-run)

public sealed record OrderResult(string OrderId, string Status, decimal Amount, DateTimeOffset CreatedAt);
```

---

## 4. Key Takeaways

1. **Zero Boilerplate**: The `IdempotencyEngine` coordinates key acquisition, lease ownership, execution, and replay serialization automatically.
2. **Transparent Replay**: The second caller receives the exact same deserialized outcome without running the delegate.
3. **Pure Port/Adapter Architecture**: `InMemoryIdempotencyStore` enables instant unit testing without database infrastructure.

---

## 5. Next Steps

Proceed to [Level 02: Complete Configuration & Options](level-02-configuration.md) to inspect every configuration knob.
