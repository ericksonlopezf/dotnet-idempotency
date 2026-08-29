# Design & Domain Model: EricksonLopez.Idempotency

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Conceptual Domain Primitives

### 1.1 `IdempotencyKey`
A strongly-typed, immutable value object representing the unique operation identifier provided by the client or message producer (maximum 128 UTF-8 characters).

### 1.2 `IdempotencyScope`
A string partition boundary (e.g. `/api/v1/payments` or `CreateOrderCommand`) that scopes key uniqueness to a specific logical operation.

### 1.3 `IdempotencyStatus`
Represents the discrete lifecycle status:
- **`Processing (1)`**: The operation is currently being executed under an active lease.
- **`Completed (2)`**: The operation was committed and completed successfully. The cached response is immutable.
- **`Failed (3)`**: The operation encountered an unhandled domain or system error.

### 1.4 `OwnerToken` & `ConcurrencyVersion`
The fencing token pair:
- `OwnerToken`: A unique `Guid` issued to the worker upon acquiring execution rights.
- `ConcurrencyVersion`: An integer incremented monotonically upon every state transition or lease takeover.

---

## 2. State Machine Transitions

```text
                      [ Initial: Key does not exist ]
                                     │
                                     │ Atomic Acquire (INSERT / Claim Stale)
                                     ▼
                     ┌───────────────────────────────┐
                     │          PROCESSING           │
                     │  (Guarded by Lease Timeout)   │
                     └───────┬───────────────┬───────┘
                             │               │
            Complete Success │               │ Execution Failed / Error
                             ▼               ▼
        ┌─────────────────────────┐     ┌─────────────────────────┐
        │        COMPLETED        │     │         FAILED          │
        │ (Immutable Cached Resp) │     │ (Eligible for Retry)    │
        └─────────────────────────┘     └────────────┬────────────┘
                                                     │
                                                     │ Re-acquire by New Request
                                                     ▼
                                        [ Transition to PROCESSING ]
```

### Invariants
1. **Uniqueness**: `(TenantId, Scope, IdempotencyKey)` is unique across the entire database.
2. **Immutability of Completed Records**: Once a record transitions to `Completed`, its cached response cannot be modified.
3. **Fencing Token Enforcement**: Only the holder of the active `(OwnerToken, ConcurrencyVersion)` can transition the record to `Completed` or `Failed`.
