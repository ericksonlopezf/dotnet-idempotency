# Storage SPI & Multi-Database Persistence

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Storage SPI Contract: IIdempotencyStore

`IIdempotencyStore` is the pure Service Provider Interface (SPI) governing distributed state coordination:

```csharp
public interface IIdempotencyStore
{
    Task<IdempotencyClaimResult> TryAcquireAsync(
        Guid tenantId,
        string scope,
        IdempotencyKey key,
        string fingerprint,
        TimeSpan leaseDuration,
        TimeSpan retentionDuration,
        CancellationToken cancellationToken = default);

    Task<bool> MarkCompletedAsync(
        Guid tenantId,
        string scope,
        IdempotencyKey key,
        Guid ownerToken,
        int concurrencyVersion,
        int statusCode,
        IReadOnlyDictionary<string, string[]> headers,
        ReadOnlyMemory<byte> responseBody,
        TimeSpan retentionDuration,
        CancellationToken cancellationToken = default);

    Task<bool> MarkFailedAsync(
        Guid tenantId,
        string scope,
        IdempotencyKey key,
        Guid ownerToken,
        int concurrencyVersion,
        CancellationToken cancellationToken = default);

    Task<int> CleanupExpiredRecordsAsync(
        DateTimeOffset utcNow,
        int batchSize,
        CancellationToken cancellationToken = default);
}
```

---

## 2. Storage Dialects Supported in the Ecosystem

| Dialect Project | Underlying Driver | Atomic Insertion Mechanism | Lease Stealing / CAS |
|---|---|---|---|
| `EricksonLopez.Idempotency.PostgreSql` | `Npgsql` | `ON CONFLICT (tenant_id, scope, idempotency_key) DO NOTHING` | `UPDATE ... WHERE ... RETURNING concurrency_version` |
| `EricksonLopez.Idempotency.SqlServer` | `Microsoft.Data.SqlClient` | `IF NOT EXISTS (...) INSERT ...` | `UPDATE ... OUTPUT INSERTED.concurrency_version` |
| `EricksonLopez.Idempotency.MySql` | `MySqlConnector` | `INSERT IGNORE INTO ...` | `UPDATE ... WHERE (status = 1 AND lease_expires_at < @Now) ...` |
| `EricksonLopez.Idempotency.MariaDb` | `MySqlConnector` | `INSERT IGNORE INTO ...` | `UPDATE ... WHERE (status = 1 AND lease_expires_at < @Now) ...` |
| `EricksonLopez.Idempotency.Oracle` | `Oracle.ManagedDataAccess.Core` | `MERGE INTO ... USING DUAL ...` | `UPDATE ... RETURNING concurrency_version INTO ...` |
| `EricksonLopez.Idempotency.Sqlite` | `Microsoft.Data.Sqlite` | `INSERT OR IGNORE INTO ...` | `UPDATE ... WHERE ... RETURNING concurrency_version` |
| `EricksonLopez.Idempotency.Redis` | `StackExchange.Redis` | Atomic Lua script (`EVAL`) checking key existence | Atomic Lua CAS verifying owner token and lease expiration |
| `EricksonLopez.Idempotency.Testing` | In-Memory Engine | `ConcurrentDictionary.TryAdd` | `ConcurrentDictionary.TryUpdate` |

---

## 3. Redis Semantics & Architectural Tradeoffs

While relational SQL providers use ACID transactions and monotonic `concurrency_version` fencing tokens in table storage, `EricksonLopez.Idempotency.Redis` executes atomic operations via server-side Lua scripts executed over `StackExchange.Redis`.

Key characteristics:
- **Atomicity**: The acquire, complete, and fail transitions run atomically inside Redis single-threaded script execution.
- **Multi-Tenancy**: Keys are formatted as `{tenantId}:{scope}:{idempotencyKey}` with TTLs mapped to retention durations.
- **Correctness Boundary**: Redis guarantees atomic claims and state transitions; however, it does not participate in relational database transactions. For transactional outbox patterns, use relational providers implementing `ITransactionalIdempotencyStore` (see [ADR-013](adr/adr-013-no-idistributedcache-abstraction.md)).

---

## 4. Database Schema Overview

```sql
CREATE TABLE idempotency_records
(
    id                        UUID                     NOT NULL,
    tenant_id                 UUID                     NOT NULL,
    scope                     VARCHAR(64)              NOT NULL,
    idempotency_key           VARCHAR(128)             NOT NULL,
    fingerprint               VARCHAR(64)              NOT NULL,
    status                    SMALLINT                 NOT NULL,
    owner_token               UUID                     NOT NULL,
    concurrency_version       INT                      NOT NULL,
    response_status_code      INT                      NULL,
    response_headers          JSONB                    NULL,
    response_body             BYTEA                    NULL,
    created_at_utc            TIMESTAMPTZ              NOT NULL,
    lease_expires_at_utc      TIMESTAMPTZ              NOT NULL,
    completed_at_utc          TIMESTAMPTZ              NULL,
    retention_expires_at_utc  TIMESTAMPTZ              NOT NULL,
    CONSTRAINT pk_idempotency_records
        PRIMARY KEY (tenant_id, scope, idempotency_key)
);

CREATE INDEX idx_idempotency_records_retention 
    ON idempotency_records (retention_expires_at_utc)
    WHERE status = 2;
```
