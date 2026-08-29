# ADR-008: Outbox Pattern Integration Flow

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

## Status
Accepted

## Context
When an API receives a command that publishes integration events via an Outbox table, network dropouts on the response must not cause duplicate events upon client retry.

## Decision
Coordinate the Outbox insertion and Idempotency record completion inside the same atomic database transaction. On re-delivery of the same key, the cached result is returned without touching the Outbox table.

## Consequences
- **Positive**: Guarantees effectively-once event publishing to external brokers.
- **Positive**: Complete transactional consistency.
