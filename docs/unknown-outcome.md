# Unknown Outcome & Network Ambiguity

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. The Two Generals Problem in Distributed APIs

When an HTTP client or background worker sends a command, network failures can happen at three distinct phases:

```text
Phase 1: Request in flight ──► [Server never received it]
Phase 2: Execution in server ─► [Server processing / committing DB]
Phase 3: Response in flight ─► [DB committed, but response packet lost!]
```

When a timeout occurs on the client, the outcome is strictly **Unknown**:
- Did the payment commit?
- Was the order created?
- Or did the connection drop before reaching the database?

---

## 2. Never Assume Timeout Equals Failure

A catastrophic anti-pattern is marking an operation as `Failed` whenever a client timeout or cancellation occurs:
```csharp
// ANTI-PATTERN:
catch (TimeoutException)
{
    // WRONG! The database transaction might have committed!
    await store.MarkFailedAsync(...); 
}
```

If the database transaction actually committed, marking the idempotency record as `Failed` allows a subsequent retry to execute the operation a second time, charging the user twice.

---

## 3. Resolving Unknown State via Replays

With `EricksonLopez.Idempotency`:
1. The server completes the business transaction and calls `MarkCompletedAsync` within the same durable storage boundary.
2. If the client timed out during Phase 3, the client retries with the **same idempotency key**.
3. The server queries the store, detects `status = Completed`, and immediately **replays the exact cached response**.
4. Result: Zero duplicate execution, zero lost data, and 100% deterministic recovery.
