# Integration with EricksonLopez.Resilience

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. The Fundamental Resilience Rule

> **Retry must repeat the same logical operation; it must never spawn duplicate side-effects.**

When combining `EricksonLopez.Resilience` (Polly-based retries, hedging, circuit breakers) with `EricksonLopez.Idempotency`:

---

## 2. Pipeline Composition Topologies

### Topology A: Resilience Outside Idempotency (Caller Retry)
The recommended topology for HTTP clients and API callers:

```text
Caller / Resilience Pipeline (Retry 3x)
   │ (Sends same Idempotency-Key on each retry)
   ▼
[EricksonLopez.Idempotency Engine]
   │
   ├── Attempt 1: Timeout after database commit (Phase 3 network failure)
   ├── Attempt 2: Replays cached outcome from Attempt 1!
   └── Attempt 3: (Not needed; Attempt 2 succeeded cleanly)
```

### Topology B: Idempotency Outside Internal Resilience (Sub-operation Retry)
Used when the operation itself calls external dependencies:

```text
[EricksonLopez.Idempotency Engine]
   │
   ▼
Business Operation
   │
   ▼
[Resilience Pipeline]
   │ (Retries 3x to call Third-Party Payment Gateway)
   ▼
Third-Party API
```

---

## 3. Safe Hedging and Idempotency

When hedging is configured (launching concurrent duplicate tasks to reduce p99 tail latency):
- Both tasks must share the **same idempotency key**.
- The second hedged task will detect the in-flight claim or cached completion and exit safely without double-charging the customer.
