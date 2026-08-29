# PostgreSQL Persistence Architecture (Dapper & Npgsql)

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. PostgreSQL Schema Definition

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

-- Partial index for high-speed retention cleanup
CREATE INDEX idx_idempotency_records_retention 
    ON idempotency_records (retention_expires_at_utc)
    WHERE status = 2;
```

---

## 2. High-Throughput Dapper Parameterized Queries

`PostgreSqlIdempotencyStore` uses Dapper with strict parameterization to maximize database query plan caching:

```csharp
private const string InsertCommandSql = """
    INSERT INTO idempotency_records (
        id, tenant_id, scope, idempotency_key, fingerprint, status,
        owner_token, concurrency_version, created_at_utc, lease_expires_at_utc, retention_expires_at_utc
    )
    VALUES (
        @Id, @TenantId, @Scope, @Key, @Fingerprint, 1,
        @OwnerToken, 1, @Now, @LeaseExpiresAt, @RetentionExpiresAt
    )
    ON CONFLICT (tenant_id, scope, idempotency_key) DO NOTHING;
    """;
```

---

## 3. High-Performance Batch Cleanup via CTID

To avoid table locks and vacuum bloat during periodic purging of expired records:

```sql
DELETE FROM idempotency_records
WHERE ctid IN (
    SELECT ctid FROM idempotency_records
    WHERE retention_expires_at_utc < @UtcNow
    LIMIT @BatchSize
);
```

---

## 4. PostgreSQL Row-Level Security (RLS)

The table includes `tenant_id UUID NOT NULL` in the composite primary key, enabling seamless integration with PostgreSQL Row-Level Security:

```sql
ALTER TABLE idempotency_records ENABLE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_policy ON idempotency_records
    FOR ALL
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::UUID);
```
