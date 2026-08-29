# ADR-004: Lease Ownership and Fencing Token Model

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

## Status
Accepted

## Context
If a processing worker crashes, is terminated by OOM, or suffers a network partition during execution, the idempotency record would remain stuck in `Processing` state indefinitely without a recovery mechanism.

## Decision
Implement a lease-based acquisition model with `lease_expires_at_utc`, `owner_token`, and monotonically increasing `concurrency_version` acting as fencing tokens. If a lease expires, another worker can atomically claim ownership.

## Consequences
- **Positive**: Automatic, safe orphan/zombie recovery without manual intervention.
- **Positive**: Stalled zombie workers are barred from overwriting subsequent successful executions.
- **Negative**: Assumes system clock synchronization (NTP) within $\pm 2$ seconds tolerance.
