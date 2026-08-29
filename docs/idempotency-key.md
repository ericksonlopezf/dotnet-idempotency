# Idempotency Key Design & Invariants

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Domain Modeling of Idempotency Keys

In distributed systems, representing an idempotency key as a naked `string` is an anti-pattern. Primitive obsession obscures validation, creates allocation bloat, and invites silent collisions through unsafe trimming.

`EricksonLopez.Idempotency` encapsulates the key as an immutable, strongly-typed Value Object:

```csharp
public readonly record struct IdempotencyKey : IEquatable<IdempotencyKey>, IComparable<IdempotencyKey>, IComparable
{
    public string Value { get; }

    public IdempotencyKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Idempotency key cannot exceed 128 characters.");
        }
        Value = value;
    }

    public static IdempotencyKey Create(string value) => new(value);
}
```

---

## 2. Invariants and Safety Guarantees

1. **Length Bounds**:
   - Minimum: 1 non-whitespace character.
   - Maximum: 128 characters (enforces index efficiency in B-Trees across PostgreSQL, SQL Server, and MySQL).
2. **Zero-Allocation Comparisons**:
   - Implemented as a readonly record struct for zero heap allocations when passed across asynchronous pipeline methods.
   - Ordinal string comparison prevents culture-dependent collation discrepancies.
3. **No Silent Normalization**:
   - Whitespace trimming is rejected at instantiation rather than silently altered. Modifying key bytes could cause two distinct client keys to alias to the same persisted record.
4. **Encoding Safety**:
   - UTF-8 and ASCII safe.

---

## 3. Client Best Practices

- **UUIDv4 / UUIDv7**: Recommended for HTTP clients (e.g. `Idempotency-Key: 9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d`).
- **Deterministic Hashes**: Deterministic generation from business values (e.g. `SHA-256(order_id + user_id)`).
- **Client Retries**: The exact same key must be sent on every retry attempt of the same logical operation.
