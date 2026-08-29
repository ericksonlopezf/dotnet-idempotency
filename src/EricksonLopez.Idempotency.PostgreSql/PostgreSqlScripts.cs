// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Idempotency.PostgreSql;

/// <summary>
/// Provides raw SQL script definitions for PostgreSQL schema generation and indexes.
/// </summary>
public static class PostgreSqlScripts
{
    /// <summary>
    /// Gets the DDL script for creating the idempotency records table and associated indexes in PostgreSQL.
    /// </summary>
    public const string CreateTableScript = """
        CREATE TABLE IF NOT EXISTS idempotency_records (
            id UUID NOT NULL,
            tenant_id UUID NOT NULL,
            scope VARCHAR(64) NOT NULL,
            idempotency_key VARCHAR(128) NOT NULL,
            fingerprint VARCHAR(64) NOT NULL,
            status SMALLINT NOT NULL, -- 1: Processing, 2: Completed, 3: Failed
            owner_token UUID NOT NULL,
            concurrency_version INT NOT NULL DEFAULT 1,
            response_status_code INT NULL,
            response_headers JSONB NULL,
            response_body BYTEA NULL,
            created_at_utc TIMESTAMPTZ NOT NULL,
            lease_expires_at_utc TIMESTAMPTZ NOT NULL,
            completed_at_utc TIMESTAMPTZ NULL,
            retention_expires_at_utc TIMESTAMPTZ NOT NULL,
            CONSTRAINT pk_idempotency_records PRIMARY KEY (tenant_id, scope, idempotency_key)
        );

        CREATE INDEX IF NOT EXISTS ix_idempotency_records_retention 
        ON idempotency_records (retention_expires_at_utc) 
        WHERE status = 2;

        CREATE INDEX IF NOT EXISTS ix_idempotency_records_stale_processing
        ON idempotency_records (lease_expires_at_utc)
        WHERE status = 1;
        """;
}
