# Level 07: Scalability, Multi-Tenancy & TTL Cleanup

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Multi-Tenant Key Partitioning

In B2B SaaS platforms, different corporate tenants often generate identical idempotency keys (e.g., `INV-001`, `PAYMENT-100`).

`EricksonLopez.Idempotency` partitions all persistence keys by a composite primary key:

$$(\text{TenantId}, \text{Scope}, \text{IdempotencyKey})$$

- **Tenant Alpha** using key `"INV-001"` will **never** collide or block **Tenant Beta** using key `"INV-001"`.
- Each tenant's data and execution lifecycles are completely isolated.

---

## 2. Multi-Tenant Isolation Code Example

```csharp
using System;
using System.Text;
using System.Threading.Tasks;
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.Testing;

var store = new InMemoryIdempotencyStore();
var sharedKey = new IdempotencyKey("INVOICE-001");
var tenantAlpha = Guid.NewGuid();
var tenantBeta = Guid.NewGuid();

var payload = Encoding.UTF8.GetBytes("{\"amount\":100}");
var fp = IdempotencyFingerprintHasher.Compute("POST", "/invoices", "tenant-alpha", null, payload);

// Tenant Alpha acquires key
var claimAlpha = await store.TryAcquireAsync(tenantAlpha, "invoices", sharedKey, fp, TimeSpan.FromMinutes(1), TimeSpan.FromDays(7));
Console.WriteLine($"Tenant Alpha acquire: Status={claimAlpha.Status}"); // AcquiredNew

// Tenant Beta acquires SAME key without colliding with Tenant Alpha
var claimBeta = await store.TryAcquireAsync(tenantBeta, "invoices", sharedKey, fp, TimeSpan.FromMinutes(1), TimeSpan.FromDays(7));
Console.WriteLine($"Tenant Beta acquire: Status={claimBeta.Status}");   // AcquiredNew
```

---

## 3. High-Throughput Batch TTL Cleanup

Completed idempotency records must eventually be purged to prevent unbounded database growth.

Every store implements `CleanupExpiredRecordsAsync(DateTimeOffset utcNow, int batchSize)`:
- Executes in bounded batches (e.g. 500–1000 records per run) to prevent database lock contention and transaction log saturation.
- Automatable via `AddIdempotencyCleanupService()` in ASP.NET Core:

```csharp
var deletedCount = await store.CleanupExpiredRecordsAsync(DateTimeOffset.UtcNow.AddDays(10), 500);
Console.WriteLine($"Purged expired records: {deletedCount}");
```

---

## 4. Next Steps

Proceed to [Level 08: Customization & Extension Points](level-08-customization.md).
