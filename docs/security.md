# Security Threat Model & Privacy Protections

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Security Threat Model

| Threat Scenario | Vector | Mitigation in EricksonLopez.Idempotency |
|---|---|---|
| **Cross-Tenant Replay Attack** | Attacker in Tenant B attempts to read payment response of Tenant A using a guessed key. | `TenantId` is baked into the composite primary key `(tenant_id, scope, idempotency_key)`. Cross-tenant queries return `NotFound`. |
| **Cross-User Replay Attack** | User B in Tenant A submits the key of User A to receive sensitive PII. | The authenticated user identifier (`sub` claim) is hashed into the SHA-256 fingerprint. Different users produce mismatched fingerprints, causing `409 Conflict`. |
| **Key Enumeration / Guessing** | Malicious client probes consecutive integer keys (`1`, `2`, `3`). | Enforces composite keys with UUIDs or nonces; fingerprint verification prevents replaying responses of different request payloads. |
| **Sensitive Header Leakage** | Storing `Authorization`, `Cookie`, or `Set-Cookie` headers in cached responses. | Sensitive and hop-by-hop headers are stripped before serialization. |
| **Storage Flooding (DoS)** | Attacker sends gigabytes of unique payload bytes to exhaust database disk. | Key length capped at 128 chars, scope capped at 64 chars, and request body size limits enforced. |

---

## 2. PII & Sensitive Response Data Handling

By default, response headers and bodies are captured for replay. When endpoints return sensitive secrets (e.g. credit card tokens, plaintext passwords, private keys):
- Use field redaction or custom serializers.
- Disable body caching for sensitive endpoints and rely on status-code-only replay.
- Implement storage encryption at rest (Transparent Data Encryption / AES-256).

---

## 3. Authorization Changes After Completion

If a user executes an idempotent command successfully, and their permissions are subsequently revoked:
- When they retry with the same key, the HTTP filter verifies authentication and authorization **before** evaluating the idempotency store.
- If the caller's JWT is expired or lacks required roles, ASP.NET Core returns `401 Unauthorized` or `403 Forbidden` before reaching the replay cache.
