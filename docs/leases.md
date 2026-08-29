# Lease Ownership, Stealing & Fencing Tokens

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. The Distributed Worker Crash Problem

In distributed systems, workers can crash, experience catastrophic network partitions, or freeze during GC pauses while processing an idempotent command.

If a worker crashes while in state `Processing`, the record must not remain permanently locked forever.

```text
Worker A claims key "ABC" (Lease: 30s)
  │
  ├─► Worker A crashes / node killed
  │
30s later (Lease Expired)
  │
Worker B receives retry with key "ABC"
  │
  └─► Worker B detects expired lease (status = 1 AND lease_expires_at < Now)
      Worker B atomically steals ownership with a new OwnerToken and incremented ConcurrencyVersion!
```

---

## 2. Atomic Lease Stealing Query

In PostgreSQL, lease stealing is executed atomically via `StealLeaseSql`:

```sql
UPDATE idempotency_records
SET owner_token = @NewOwnerToken,
    concurrency_version = concurrency_version + 1,
    status = 1,
    fingerprint = @Fingerprint,
    lease_expires_at_utc = @NewLeaseExpiresAt,
    created_at_utc = @Now
WHERE tenant_id = @TenantId
  AND scope = @Scope
  AND idempotency_key = @Key
  AND (
      (status = 1 AND lease_expires_at_utc < @Now)
      OR status = 3
  )
RETURNING concurrency_version;
```

---

## 3. Fencing Tokens: Preventing Zombie Worker Overwrite

Suppose Worker A did not crash, but suffered a 45-second network hiccup:
1. Worker A claimed key with $v = 1$.
2. Lease expired at $t = 30\text{s}$.
3. Worker B stole lease at $t = 35\text{s}$ with $v = 2$ and executed the business logic.
4. Worker A wakes up at $t = 45\text{s}$ and attempts `MarkCompletedAsync` with $v = 1$.

```sql
UPDATE idempotency_records
SET status = 2, ...
WHERE tenant_id = @TenantId
  AND scope = @Scope
  AND idempotency_key = @Key
  AND owner_token = @OwnerTokenA   -- Does not match Worker B's token!
  AND concurrency_version = 1;     -- Does not match version 2!
```

`rowsAffected == 0`. Worker A's write is rejected, preventing data corruption.
