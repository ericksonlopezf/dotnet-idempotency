// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Idempotency.MariaDb;

/// <summary>
/// Provides raw SQL script definitions for MariaDB schema generation and indexes.
/// </summary>
public static class MariaDbScripts
{
    /// <summary>
    /// Gets the DDL script for creating the idempotency records table and associated indexes in MariaDB.
    /// </summary>
    public const string CreateTableScript = """
        CREATE TABLE IF NOT EXISTS idempotency_records (
            id UUID NOT NULL,
            tenant_id UUID NOT NULL,
            scope VARCHAR(64) NOT NULL,
            idempotency_key VARCHAR(128) NOT NULL,
            fingerprint VARCHAR(64) NOT NULL,
            status TINYINT NOT NULL, -- 1: Processing, 2: Completed, 3: Failed
            owner_token UUID NOT NULL,
            concurrency_version INT NOT NULL DEFAULT 1,
            response_status_code INT NULL,
            response_headers LONGTEXT NULL,
            response_body LONGBLOB NULL,
            created_at_utc DATETIME(6) NOT NULL,
            lease_expires_at_utc DATETIME(6) NOT NULL,
            completed_at_utc DATETIME(6) NULL,
            retention_expires_at_utc DATETIME(6) NOT NULL,
            PRIMARY KEY (tenant_id, scope, idempotency_key),
            INDEX ix_idempotency_records_retention (retention_expires_at_utc, status),
            INDEX ix_idempotency_records_stale_processing (lease_expires_at_utc, status)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        """;
}
