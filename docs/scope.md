# Idempotency Scope and Namespacing

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Why Idempotency Keys Need Scopes

A bare idempotency key (e.g. `ABC123`) is not globally unique across different business operations or endpoints.

Consider the following collision scenario if keys were globally scoped:

```text
POST /api/v1/payments (Key: "TX-1001") ──► Creates Payment
POST /api/v1/refunds  (Key: "TX-1001") ──► ACCIDENTALLY REPLAYS PAYMENT RESPONSE!
```

Without functional boundaries, different operations sharing client-generated keys will collide catastrophically.

---

## 2. The IdempotencyScope Value Object

`EricksonLopez.Idempotency` mandates a functional `IdempotencyScope`:

```csharp
public readonly record struct IdempotencyScope : IEquatable<IdempotencyScope>, IComparable<IdempotencyScope>, IComparable
{
    public static readonly IdempotencyScope Default = new("default");
    public string Value { get; }

    public IdempotencyScope(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Idempotency scope cannot exceed 64 characters.");
        }
        Value = value;
    }
}
```

---

## 3. Scope Resolution Strategies

The primary key of an idempotency record is composed of a 3-part composite identifier:

$$\text{Composite Key} = (\text{TenantId}, \text{Scope}, \text{IdempotencyKey})$$

| Context | Scope Formulation | Example |
|---|---|---|
| **Minimal APIs** | Route pattern or custom name | `payments` or `/api/v1/orders` |
| **Controller Actions** | `[Idempotent(Scope = "billing")]` | `billing` |
| **Mediator Commands** | Fully qualified command type name | `MyApp.Billing.CreateInvoiceCommand` |
| **Messaging Consumers** | Message contract topic/type | `OrderCreatedEvent` |

---

## 4. Multi-Tenant Isolation

Scopes work in tandem with `TenantId` (GUID). Even if Tenant A and Tenant B use the exact same key (`TX-1001`) within the same scope (`payments`), their records are partitioned and isolated in the storage engine:

```sql
SELECT * FROM idempotency_records
WHERE tenant_id = @TenantId
  AND scope = @Scope
  AND idempotency_key = @Key;
```
