# ADR-005: PostgreSQL and Dapper with Raw SQL Persistence

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

## Status
Accepted

## Context
High-throughput idempotency stores require maximum execution plan predictability, atomic concurrency statements (`ON CONFLICT`, `RETURNING`), and zero runtime reflection overhead for Native AOT.

## Decision
Implement `PostgreSqlIdempotencyStore` using static raw SQL parameterized queries and Dapper over `NpgsqlDataSource`.

## Consequences
- **Positive**: Maximum throughput and sub-millisecond query execution.
- **Positive**: Native AOT compatibility and zero reflection overhead.
