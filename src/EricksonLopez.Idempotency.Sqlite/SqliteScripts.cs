// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Idempotency.Sqlite;

/// <summary>
/// Provides raw SQL script definitions for SQLite schema generation and indexes.
/// </summary>
public static class SqliteScripts
{
    /// <summary>
    /// Gets the DDL script for creating the idempotency records table and associated indexes in SQLite.
    /// </summary>
    public const string CreateTableScript = """
        CREATE TABLE IF NOT EXISTS idempotency_records (
            id TEXT NOT NULL,
            tenant_id TEXT NOT NULL,
            scope TEXT NOT NULL,
            idempotency_key TEXT NOT NULL,
            fingerprint TEXT NOT NULL,
            status INTEGER NOT NULL, -- 1: Processing, 2: Completed, 3: Failed
            owner_token TEXT NOT NULL,
            concurrency_version INTEGER NOT NULL DEFAULT 1,
            response_status_code INTEGER NULL,
            response_headers TEXT NULL,
            response_body BLOB NULL,
            created_at_utc TEXT NOT NULL,
            lease_expires_at_utc TEXT NOT NULL,
            completed_at_utc TEXT NULL,
            retention_expires_at_utc TEXT NOT NULL,
            PRIMARY KEY (tenant_id, scope, idempotency_key)
        );

        CREATE INDEX IF NOT EXISTS ix_idempotency_records_retention 
            ON idempotency_records (retention_expires_at_utc, status);

        CREATE INDEX IF NOT EXISTS ix_idempotency_records_stale_processing 
            ON idempotency_records (lease_expires_at_utc, status);
        """;
}
