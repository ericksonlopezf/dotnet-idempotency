# ADR-002: Separation of Idempotency from Resilience (Polly)

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

## Status
Accepted

## Context
There is often confusion between idempotency (ensuring an operation produces the same effect when repeated) and resilience (policies for retrying, backoff, circuit breaking).

## Decision
`EricksonLopez.Idempotency` will **not** implement retry policies, circuit breakers, or rate limiting. It solely provides server-side deduplication, record storage, and lease ownership.

## Consequences
- **Positive**: High architectural cohesion and zero overlap with resilience libraries (Polly).
- **Positive**: Lightweight Core.
