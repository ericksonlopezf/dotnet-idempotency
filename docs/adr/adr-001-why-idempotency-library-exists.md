# ADR-001: Creation of EricksonLopez.Idempotency Framework

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

## Status
Accepted

## Context
In distributed microservices and HTTP APIs, network dropped connections and retries cause duplicate execution of side-effecting operations (such as payments or order creation). Existing solutions either rely on in-memory caches, generic Redis distributed locks that do not store responses, or tightly coupled ASP.NET filters without Native AOT support.

## Decision
Create `EricksonLopez.Idempotency` as an independent, Native AOT-first, PostgreSQL and Dapper powered idempotency framework adhering strictly to Clean Architecture and DDD principles.

## Consequences
- **Positive**: Guaranteed effectively-once execution semantics across distributed nodes.
- **Positive**: Native AOT trimming compatibility and zero reflection overhead.
- **Negative**: Adds a storage overhead for caching completed response payloads.
