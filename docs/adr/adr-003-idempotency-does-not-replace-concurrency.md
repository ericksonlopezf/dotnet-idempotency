# ADR-003: Idempotency Does Not Replace Business Concurrency

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

## Status
Accepted

## Context
Two distinct requests with different idempotency keys can target the same aggregate root (e.g. two transfers debiting the same bank account).

## Decision
Idempotency manages the deduplication of the *same logical operation*. Domain concurrency invariants continue to be guarded by Optimistic Concurrency Control (`version` column) on Aggregate Roots.

## Consequences
- **Positive**: Strict separation of concerns between operational replay and entity invariant protection.
