# Level 05: High Concurrency & Race Conditions

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. The Race Condition Problem

In high-throughput microservice architectures, clients frequently fire duplicate requests simultaneously within milliseconds:

```
Thread 1 ──[ POST /orders (Key: "KEY-1") ]──┐
Thread 2 ──[ POST /orders (Key: "KEY-1") ]──┼──> [ Server Receives All at T=0ms ]
Thread 3 ──[ POST /orders (Key: "KEY-1") ]──┘
```

Without strict atomicity, all threads might check the database, find no completed record, and proceed to execute the business handler simultaneously.

---

## 2. Atomic Lease Winning Mechanism

`EricksonLopez.Idempotency` solves this via atomic database primitives:
- **PostgreSQL**: `INSERT INTO idempotency_records (...) ON CONFLICT (tenant_id, scope, idempotency_key) DO NOTHING;`
- **SQL Server**: `MERGE WITH (HOLDLOCK) / IF NOT EXISTS`
- **MySQL / MariaDB**: `INSERT IGNORE INTO idempotency_records ...`
- **SQLite**: `INSERT OR IGNORE INTO idempotency_records ...`
- **Redis**: Atomic Lua scripts with single-threaded Redis execution

### Guarantee
Exactly **one thread** acquires the lease with status `AcquiredNew`.  
All remaining threads receive `ClaimResultStatus.InFlightConflict` and HTTP 409 Conflict with `Retry-After: 1`.

---

## 3. High-Concurrency Stress Test (20 Threads Racing)

```csharp
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
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

var key = new IdempotencyKey("RACE-KEY-1");
var fp = IdempotencyFingerprintHasher.Compute("POST", "/tickets", "tenant-1", null, Encoding.UTF8.GetBytes("{\"seat\":\"A12\"}"));

int actualBusinessExecutions = 0;
int successfulResponses = 0;
int conflictCount = 0;

var tasks = new List<Task>();

for (int i = 0; i < 20; i++)
{
    tasks.Add(Task.Run(async () =>
    {
        try
        {
            var res = await engine.ExecuteAsync(Guid.Empty, "tickets", key, fp, async ct =>
            {
                // Simulate processing latency
                await Task.Delay(100, ct);
                Interlocked.Increment(ref actualBusinessExecutions);
                return new TicketResult("TICKET-A12", "Confirmed");
            });

            Interlocked.Increment(ref successfulResponses);
        }
        catch (IdempotencyConflictException)
        {
            Interlocked.Increment(ref conflictCount);
        }
    }));
}

await Task.WhenAll(tasks);

Console.WriteLine($"Total Concurrent Requests: 20");
Console.WriteLine($"Actual Business Executions: {actualBusinessExecutions}"); // Exactly 1
Console.WriteLine($"In-Flight 409 Conflicts: {conflictCount}");              // Exactly 19

public sealed record TicketResult(string TicketId, string Status);
```

---

## 4. Next Steps

Proceed to [Level 06: Fault Tolerance & Zombie Worker Recovery](level-06-fault-tolerance.md).
