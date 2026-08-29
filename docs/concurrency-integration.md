# Integration with EricksonLopez.Concurrency

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Distinct Architectural Responsibilities

The EricksonLopez ecosystem explicitly decouples idempotency from concurrency:

```text
EricksonLopez.Idempotency:
  "Has this exact logical operation been invoked before with this IdempotencyKey?"
  Guarantees: At-most-once execution and safe outcome replay.

EricksonLopez.Concurrency:
  "Has the state of this entity or aggregate changed since the caller loaded it?"
  Guarantees: Optimistic/pessimistic state invariants and lost-update prevention.
```

---

## 2. Interaction Pipeline

```text
Request (Key: "KEY-1", ExpectedVersion: 5)
   │
   ▼
[Idempotency Engine]
   │
   ├── [Claim: Completed] ──► Replays previously cached response immediately.
   │
   └── [Claim: New]
            │
            ▼
       [Domain Service / Concurrency Check]
            │
            ├── Aggregated state is version 6 (stale read!)
            │
            ▼
       Throws ConcurrencyConflictException / Returns Result.Conflict
            │
            ▼
       [Idempotency Engine]
            │
            └── MarkFailedAsync (Enables subsequent retries with updated version).
```

---

## 3. Concurrency Conflict Propagation

When a brand-new idempotent request fails due to an aggregate concurrency conflict:
- Idempotency **must not** cache a false success.
- Idempotency **must not** permanently block future attempts with the same key if the application policy allows retry after failure.
- Marking the record as `Failed` allows resilience policies (Polly / `EricksonLopez.Resilience`) to reload aggregate version 6 and retry cleanly.
