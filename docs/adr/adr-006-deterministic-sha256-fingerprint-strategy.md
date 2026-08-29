# ADR-006: Deterministic SHA-256 Cryptographic Fingerprint Strategy

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

## Status
Accepted

## Context
Malicious or buggy clients might reuse the same `Idempotency-Key` with different request payloads (e.g. changing the transaction amount).

## Decision
Compute a canonical SHA-256 hash incorporating all five canonical components in order:
`OperationName + ':' + Scope + ':' + TenantId + ':' + AuthenticatedSubject + ':' + PayloadBytes`.
The hash is returned as an **uppercase hexadecimal** string via `Convert.ToHexString()`.
Any mismatch on subsequent requests with the same key is rejected with `HTTP 409 Conflict`.

## Consequences
- **Positive**: Complete prevention of payload tampering and accidental key collision.
- **Positive**: Minimal heap allocations — `stackalloc` is used for inputs ≤ 256 bytes; longer inputs fall back to heap allocation. `Convert.ToHexString` always allocates the result string.
