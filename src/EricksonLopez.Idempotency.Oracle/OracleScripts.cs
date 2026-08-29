// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Idempotency.Oracle;

/// <summary>
/// Provides raw SQL script definitions for Oracle schema generation and indexes.
/// </summary>
public static class OracleScripts
{
    /// <summary>
    /// Gets the DDL script for creating the idempotency records table and associated indexes in Oracle Database.
    /// </summary>
    public const string CreateTableScript = """
        BEGIN
            EXECUTE IMMEDIATE '
                CREATE TABLE idempotency_records (
                    id RAW(16) NOT NULL,
                    tenant_id RAW(16) NOT NULL,
                    scope VARCHAR2(64) NOT NULL,
                    idempotency_key VARCHAR2(128) NOT NULL,
                    fingerprint VARCHAR2(64) NOT NULL,
                    status NUMBER(3) NOT NULL,
                    owner_token RAW(16) NOT NULL,
                    concurrency_version NUMBER(10) DEFAULT 1 NOT NULL,
                    response_status_code NUMBER(10) NULL,
                    response_headers CLOB NULL,
                    response_body BLOB NULL,
                    created_at_utc TIMESTAMP WITH TIME ZONE NOT NULL,
                    lease_expires_at_utc TIMESTAMP WITH TIME ZONE NOT NULL,
                    completed_at_utc TIMESTAMP WITH TIME ZONE NULL,
                    retention_expires_at_utc TIMESTAMP WITH TIME ZONE NOT NULL,
                    CONSTRAINT pk_idempotency_records PRIMARY KEY (tenant_id, scope, idempotency_key)
                )';
        EXCEPTION
            WHEN OTHERS THEN
                IF SQLCODE != -955 THEN
                    RAISE;
                END IF;
        END;
        """;
}
