# Integration Guide: EricksonLopez.Idempotency

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Outbox Pattern Coordination

When combining Idempotency with transactional business operations and the Outbox pattern:

```text
[ Incoming Request ]
        │
        ▼
   [ 1. Acquire Key: Status = Processing (Independent Claim) ]
        │
        ▼
   [ 2. Begin Business Transaction (Unit of Work) ]
   ├─── Write Domain Entities (e.g. Orders Table)
   ├─── Write Outbox Messages (e.g. Outbox Table)
   ├─── Stage Idempotency Record (Status = Completed, CachedBody = ResponseDto)
   [ 3. Commit Business Transaction (Atomic Commit) ]
        │
        ▼
   [ 4. Return Response to Client ]
```

### Critical Resiliency Scenarios
1. **Network Dropout After Database Commit**:
   - The business transaction, outbox messages, and idempotency status are committed together.
   - When the client re-submits the request, the server immediately returns the cached response without re-executing business logic or creating duplicate outbox messages.
2. **Transaction Rollback on Business Failure**:
   - Outbox messages are rolled back.
   - The idempotency record is marked `Failed`, allowing subsequent retries.

---

## 2. Mediator Pipeline Ordering

For `EricksonLopez.Mediator`, the pipeline behaviors must be configured in this precise order:

```text
1. Logging / OpenTelemetry Behavior
2. IdempotencyPipelineBehavior
3. ValidationBehavior
4. TransactionBehavior (Unit of Work)
5. CommandHandler
```
