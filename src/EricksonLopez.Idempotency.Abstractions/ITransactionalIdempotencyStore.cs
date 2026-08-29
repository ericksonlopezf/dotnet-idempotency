// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Idempotency;

/// <summary>
/// Extends <see cref="IIdempotencyStore"/> with the ability to participate in an existing database transaction,
/// enabling atomic coordination with domain operations in the Outbox and CQRS transactional patterns.
/// </summary>
/// <remarks>
/// <para>
/// Implementing stores that use SQL databases (PostgreSQL, SQL Server, MySQL, MariaDB, SQLite, Oracle)
/// implement this interface to allow the caller to share an existing <see cref="IDbConnection"/> and
/// optional <see cref="IDbTransaction"/> with the idempotency store operations.
/// </para>
/// <para>
/// When both <c>connection</c> and <c>transaction</c> are provided, the store must use them directly
/// without opening a new connection. This ensures that the idempotency record mutation participates in the
/// same database transaction as the caller's domain operation.
/// </para>
/// <para>
/// When the caller does not provide a connection/transaction, implementations fall back to opening their
/// own connection, preserving backward-compatible behavior.
/// </para>
/// <para>
/// See <c>docs/transaction-integration.md</c> and <c>docs/adr/adr-011-transactional-store-participation.md</c>
/// for the architectural rationale and usage patterns.
/// </para>
/// <example>
/// <code lang="csharp">
/// // Outbox + Idempotency atomic pattern
/// await using var conn = await dataSource.OpenConnectionAsync(ct);
/// await using var tx = await conn.BeginTransactionAsync(ct);
///
/// // 1. Domain operation
/// await domainRepository.SaveAsync(order, conn, tx, ct);
///
/// // 2. Outbox message
/// await outboxWriter.WriteAsync(orderPlacedEvent, conn, tx, ct);
///
/// // 3. Mark idempotency as completed — same transaction
/// if (store is ITransactionalIdempotencyStore txStore)
/// {
///     await txStore.MarkCompletedAsync(tenantId, scope, key, ownerToken, version,
///         statusCode, headers, body, retention, conn, tx, ct);
/// }
///
/// await tx.CommitAsync(ct);
/// </code>
/// </example>
/// </remarks>
public interface ITransactionalIdempotencyStore : IIdempotencyStore
{
    /// <summary>
    /// Atomically marks an in-flight idempotency record as completed within the provided database transaction,
    /// storing the produced response payload.
    /// </summary>
    /// <param name="tenantId">The unique identifier of the tenant.</param>
    /// <param name="scope">The functional scope of the operation.</param>
    /// <param name="key">The idempotency key.</param>
    /// <param name="ownerToken">The ownership token issued during key acquisition.</param>
    /// <param name="concurrencyVersion">The concurrency version issued during key acquisition.</param>
    /// <param name="statusCode">The HTTP or logical status code to cache.</param>
    /// <param name="headers">The response headers to cache.</param>
    /// <param name="responseBody">The serialized response body bytes to cache.</param>
    /// <param name="retentionDuration">The retention duration for the completed record.</param>
    /// <param name="connection">
    /// An existing open <see cref="IDbConnection"/> to use. Must not be <see langword="null"/>.
    /// The store will use this connection without opening a new one.
    /// </param>
    /// <param name="transaction">
    /// An optional active <see cref="IDbTransaction"/> the operation should participate in.
    /// If <see langword="null"/>, the operation runs without a transaction but still uses the provided connection.
    /// </param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains <see langword="true"/> if the record was
    /// successfully updated; otherwise, <see langword="false"/> (fencing token mismatch or owner mismatch).
    /// </returns>
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
        IDbConnection connection,
        IDbTransaction? transaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically marks an in-flight idempotency record as failed within the provided database transaction,
    /// enabling subsequent retries depending on policy.
    /// </summary>
    /// <param name="tenantId">The unique identifier of the tenant.</param>
    /// <param name="scope">The functional scope of the operation.</param>
    /// <param name="key">The idempotency key.</param>
    /// <param name="ownerToken">The ownership token issued during key acquisition.</param>
    /// <param name="concurrencyVersion">The concurrency version issued during key acquisition.</param>
    /// <param name="connection">
    /// An existing open <see cref="IDbConnection"/> to use. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="transaction">
    /// An optional active <see cref="IDbTransaction"/> the operation should participate in.
    /// If <see langword="null"/>, the operation runs without a transaction but still uses the provided connection.
    /// </param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains <see langword="true"/> if the record
    /// was successfully marked as failed; otherwise, <see langword="false"/>.
    /// </returns>
    Task<bool> MarkFailedAsync(
        Guid tenantId,
        string scope,
        IdempotencyKey key,
        Guid ownerToken,
        int concurrencyVersion,
        IDbConnection connection,
        IDbTransaction? transaction,
        CancellationToken cancellationToken = default);
}
