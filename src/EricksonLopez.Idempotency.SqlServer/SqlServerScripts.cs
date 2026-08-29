// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Idempotency.SqlServer;

/// <summary>
/// Provides raw SQL script definitions for SQL Server schema generation and indexes.
/// </summary>
public static class SqlServerScripts
{
    /// <summary>
    /// Gets the DDL script for creating the idempotency records table and associated indexes in Microsoft SQL Server.
    /// </summary>
    public const string CreateTableScript = """
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'idempotency_records')
        BEGIN
            CREATE TABLE idempotency_records (
                id UNIQUEIDENTIFIER NOT NULL,
                tenant_id UNIQUEIDENTIFIER NOT NULL,
                scope NVARCHAR(64) NOT NULL,
                idempotency_key NVARCHAR(128) NOT NULL,
                fingerprint NVARCHAR(64) NOT NULL,
                status TINYINT NOT NULL, -- 1: Processing, 2: Completed, 3: Failed
                owner_token UNIQUEIDENTIFIER NOT NULL,
                concurrency_version INT NOT NULL DEFAULT 1,
                response_status_code INT NULL,
                response_headers NVARCHAR(MAX) NULL,
                response_body VARBINARY(MAX) NULL,
                created_at_utc DATETIMEOFFSET NOT NULL,
                lease_expires_at_utc DATETIMEOFFSET NOT NULL,
                completed_at_utc DATETIMEOFFSET NULL,
                retention_expires_at_utc DATETIMEOFFSET NOT NULL,
                CONSTRAINT pk_idempotency_records PRIMARY KEY (tenant_id, scope, idempotency_key)
            );

            CREATE NONCLUSTERED INDEX ix_idempotency_records_retention 
            ON idempotency_records (retention_expires_at_utc) 
            WHERE status = 2;

            CREATE NONCLUSTERED INDEX ix_idempotency_records_stale_processing
            ON idempotency_records (lease_expires_at_utc)
            WHERE status = 1;
        END;
        """;
}
