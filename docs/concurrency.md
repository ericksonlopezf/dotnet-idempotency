# Concurrency Control & Race Condition Management

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Concurrent Duplicates Challenge

When 100 concurrent requests with the identical `(TenantId, Scope, IdempotencyKey)` arrive simultaneously:
- Exactly **one** request must win ownership and execute the underlying business logic.
- Exactly **99** requests must detect that an operation is already in-flight or completed, avoiding duplicate execution.

```text
Request A (Key: "ABC") ──┐
Request B (Key: "ABC") ──┼──► [Atomic Store Claim] ──► Winner: Request A (AcquiredNew) ──► Executes Handler
Request C (Key: "ABC") ──┘                         ──► Loser:  Request B, C (InFlightConflict) ──► Yields 409 Conflict
```

---

## 2. No Read-Then-Insert Race Conditions

A naive implementation that does `SELECT ... THEN INSERT` suffers from severe race conditions under concurrency:

```text
[Thread 1] SELECT ... -> Not Found
[Thread 2] SELECT ... -> Not Found
[Thread 1] INSERT ... -> Success (Executes business operation)
[Thread 2] INSERT ... -> Crash or duplicate execution!
```

`EricksonLopez.Idempotency` guarantees atomicity using native database engine conflict handling:
- **PostgreSQL**: `INSERT INTO idempotency_records (...) VALUES (...) ON CONFLICT (tenant_id, scope, idempotency_key) DO NOTHING;`
- **Rows Affected = 1**: The current worker is the single, undisputed owner.
- **Rows Affected = 0**: The record already existed. The worker queries the record to determine if it is currently processing or completed.

---

## 3. Difference Between Concurrency and Idempotency

It is critical to separate these two concepts:

```text
Idempotency:
  "Has this exact logical operation been initiated before with this idempotency key?"
  Protection: Duplicate command execution and repeated side-effects.

Concurrency (Optimistic Concurrency / EricksonLopez.Concurrency):
  "Has the state of this aggregate root changed since I read it?"
  Protection: Lost updates and stale reads.
```

An idempotent request can still encounter a concurrency conflict if another concurrent operation altered the aggregate.
