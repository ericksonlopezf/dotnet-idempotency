# Level 06: Fault Tolerance & Zombie Worker Recovery

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. The Zombie Worker Problem

Consider what happens when a worker server claims an idempotency key and **abruptly crashes midway** (e.g., Out Of Memory, SIGKILL, hardware failure):

```
Worker 1: Claims "KEY-1" (Status = Processing, Lease = 30s)
Worker 1: [ SERVER CRASHES / HARDWARE FAULT ]
Client:   Retries after 35 seconds...
```

If the key remains permanently locked in `Processing` state, the client will be locked out forever with perpetual 409 Conflict errors!

---

## 2. Monotonic Fencing Tokens & Lease Stealing

`EricksonLopez.Idempotency` solves this via **time-bounded ownership leases + monotonic fencing tokens**:

1. Every claim receives a lease duration (e.g. 30 seconds) and a `ConcurrencyVersion` (e.g. `1`).
2. When the lease expires (`lease_expires_at_utc < now`), the key becomes eligible for **lease stealing**.
3. A subsequent worker atomically steals the lease:
   - Issues a new `OwnerToken` GUID.
   - Monotonically increments the `ConcurrencyVersion` from `1` to `2`.
   - Resets the lease expiration timer.
4. If the crashed Worker 1 somehow recovers and attempts to call `MarkCompletedAsync` with `version = 1`, the database atomic update fails (`rows_affected = 0`), preventing zombie writes.

---

## 3. Crash Recovery Demonstration

```csharp
using System;
using System.Text;
using System.Threading.Tasks;
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.Testing;

var store = new InMemoryIdempotencyStore();
var key = new IdempotencyKey("CRASH-RECOVER-01");
var tenantId = Guid.NewGuid();
var fp = IdempotencyFingerprintHasher.Compute("POST", "/orders", tenantId.ToString(), null, Encoding.UTF8.GetBytes("{\"id\":\"O-1\"}"));

// 1. Worker 1 acquires lease for 500ms
var claim1 = await store.TryAcquireAsync(tenantId, "orders", key, fp, TimeSpan.FromMilliseconds(500), TimeSpan.FromDays(7));
Console.WriteLine($"Worker 1 Claim: Status={claim1.Status}, Version={claim1.ConcurrencyVersion}"); // Status: AcquiredNew, Version: 1

// 2. Worker 1 crashes (never calls MarkCompletedAsync)

// 3. Worker 2 attempts to claim immediately (within TTL) -> InFlightConflict
var prematureClaim = await store.TryAcquireAsync(tenantId, "orders", key, fp, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));
Console.WriteLine($"Worker 2 premature claim: Status={prematureClaim.Status}"); // Status: InFlightConflict

// 4. Wait for lease to expire (600ms)
await Task.Delay(600);

// 5. Worker 2 retries after expiration -> Steals lease with incremented version
var recoveryClaim = await store.TryAcquireAsync(tenantId, "orders", key, fp, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7));
Console.WriteLine($"Worker 2 recovery claim: Status={recoveryClaim.Status}"); // Status: AcquiredStale
Console.WriteLine($"New Version: {recoveryClaim.ConcurrencyVersion} (Incremented from {claim1.ConcurrencyVersion})");
```

---

## 4. Next Steps

Proceed to [Level 07: Scalability & Multi-Tenancy](level-07-scalability-multitenancy.md).
