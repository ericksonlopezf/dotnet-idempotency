# Integration with EricksonLopez.Outbox

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. The Dual-Write Problem

A common architectural disaster in distributed systems is updating business state and emitting integration events without transactional atomicity:

```text
Step 1: Save Order in DB ─────────► [Success]
Step 2: Publish Event to Kafka ───► [Failed / Process Crash!]
==> System is now in an inconsistent, corrupt state.
```

The **Transactional Outbox Pattern** (`EricksonLopez.Outbox`) solves this by persisting domain events in the same relational database transaction as the business state.

---

## 2. The Duplicate Outbox Event Dilemma

If an idempotent operation is retried by a client, and idempotency is evaluated **after** the transaction:

```text
Retry with same key arrives
  │
  ├─► Creates duplicate Order
  ├─► Inserts duplicate Outbox Message
  └─► Broker publishes duplicate events downstream!
```

---

## 3. The Unified Transactional Architecture

By coordinating `Idempotency + Transactions + Outbox`:

```text
Request (Key: "ABC")
   │
   ▼
[Idempotency Engine]
   │
   ├── [Case 1: Already Completed] ──► Replay Cached Response (No new order, NO duplicate Outbox message!)
   │
   └── [Case 2: New Operation]
            │
            ▼
       BEGIN TRANSACTION
            │
            ├── 1. Insert Order Aggregate
            ├── 2. Insert Outbox Event (OrderCreatedEvent)
            └── 3. Mark Idempotency Record Completed (with serialized response)
            │
       COMMIT TRANSACTION
```

### Result:
When the client retries with key `"ABC"`, the server returns the cached response immediately. Zero new domain events are inserted into the outbox table.
