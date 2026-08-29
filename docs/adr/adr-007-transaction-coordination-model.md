# ADR-007: Transaction Coordination Model

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

## Status
Accepted

## Context
When business domain changes and idempotency state exist within the same relational database, dual-write anomalies can occur if the business transaction succeeds but the idempotency record update fails.

## Decision
Support co-located transactional persistence where the idempotency completion status participates in the same atomic `IUnitOfWork` database transaction as domain entity mutations and Outbox messages.

## Consequences
- **Positive**: 100% atomic consistency between business changes and cached idempotency state.
- **Positive**: Elimination of partial failure states.
