// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Idempotency.Exceptions;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.Idempotency;

/// <summary>
/// Coordinates the execution workflow and state machine transitions for idempotent operations.
/// </summary>
public sealed partial class IdempotencyEngine
{
    private readonly IIdempotencyStore _store;
    private readonly IIdempotencyPolicy _policy;
    private readonly IIdempotencySerializer _serializer;
    private readonly IIdempotencyContextAccessor _contextAccessor;
    private readonly ILogger<IdempotencyEngine> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyEngine"/> class with the specified dependencies.
    /// </summary>
    /// <param name="store">The persistence store for recording idempotency state.</param>
    /// <param name="policy">The evaluation policy determining lease and retention rules.</param>
    /// <param name="serializer">The serializer used for response caching.</param>
    /// <param name="contextAccessor">The accessor for current idempotency context.</param>
    /// <param name="logger">The logger for diagnostics and runtime events.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/>, <paramref name="policy"/>, <paramref name="serializer"/>, <paramref name="contextAccessor"/>, or <paramref name="logger"/> is <see langword="null"/></exception>
    public IdempotencyEngine(
        IIdempotencyStore store,
        IIdempotencyPolicy policy,
        IIdempotencySerializer serializer,
        IIdempotencyContextAccessor contextAccessor,
        ILogger<IdempotencyEngine> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes an operation idempotently, ensuring effectively-once execution semantics.
    /// </summary>
    /// <typeparam name="TResult">The operation result payload type.</typeparam>
    /// <param name="tenantId">The unique tenant identifier.</param>
    /// <param name="scope">The functional scope of the operation.</param>
    /// <param name="key">The idempotency key.</param>
    /// <param name="fingerprint">The cryptographic fingerprint of the request.</param>
    /// <param name="operation">The asynchronous operation delegate to execute.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains the freshly computed
    /// result if ownership was acquired, or the deserialized cached value if the operation was already completed.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/></exception>
    /// <exception cref="IdempotencyFingerprintMismatchException">The key was previously used with a different request fingerprint</exception>
    /// <exception cref="IdempotencyConflictException">An identical operation is currently in-flight and executing</exception>
    public async Task<TResult> ExecuteAsync<TResult>(
        Guid tenantId,
        string scope,
        IdempotencyKey key,
        string fingerprint,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        using var activity = IdempotencyDiagnostics.ActivitySource.StartActivity("Idempotency.Execute");
        activity?.SetTag("idempotency.scope", scope);
        activity?.SetTag("idempotency.tenant_id", tenantId.ToString());

        IdempotencyDiagnostics.RecordRequest(scope);

        var claim = await _store.TryAcquireAsync(
            tenantId,
            scope,
            key,
            fingerprint,
            _policy.LeaseDuration,
            _policy.RetentionDuration,
            cancellationToken).ConfigureAwait(false);

        if (claim.Status == ClaimResultStatus.FingerprintMismatch)
        {
            IdempotencyDiagnostics.RecordFingerprintMismatch(scope);
            activity?.SetStatus(ActivityStatusCode.Error, "Fingerprint mismatch");
            throw new IdempotencyFingerprintMismatchException(key.Value, claim.ExistingFingerprint, fingerprint);
        }

        if (claim.Status == ClaimResultStatus.InFlightConflict)
        {
            IdempotencyDiagnostics.RecordDuplicate(scope);
            IdempotencyDiagnostics.RecordConflict(scope);
            activity?.SetStatus(ActivityStatusCode.Error, "In-flight conflict");
            throw new IdempotencyConflictException(key.Value);
        }

        if (claim.Status == ClaimResultStatus.CompletedReplay && claim.CachedResponse != null)
        {
            IdempotencyDiagnostics.RecordDuplicate(scope);
            IdempotencyDiagnostics.RecordReplayed(scope);
            activity?.SetTag("idempotency.replayed", true);

            var cachedValue = _serializer.Deserialize<TResult>(claim.CachedResponse.Body);
            return cachedValue!;
        }

        var context = new IdempotencyContext
        {
            TenantId = tenantId,
            Scope = scope,
            Key = key,
            OwnerToken = claim.OwnerToken,
            ConcurrencyVersion = claim.ConcurrencyVersion,
            IsReplay = false
        };

        _contextAccessor.IdempotencyContext = context;

        try
        {
            IdempotencyDiagnostics.RecordExecution(scope);
            var result = await operation(cancellationToken).ConfigureAwait(false);

            var serializedBody = _serializer.Serialize(result);

            var completed = await _store.MarkCompletedAsync(
                tenantId,
                scope,
                key,
                claim.OwnerToken!.Value,
                claim.ConcurrencyVersion!.Value,
                200,
                new Dictionary<string, string[]>(),
                serializedBody,
                _policy.RetentionDuration,
                CancellationToken.None).ConfigureAwait(false);

            if (!completed)
            {
                LogLeaseLost(_logger, key.Value, scope);
            }
            else
            {
                IdempotencyDiagnostics.RecordCompleted(scope);
            }

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            IdempotencyDiagnostics.RecordFailed(scope);

            if (claim.OwnerToken.HasValue && claim.ConcurrencyVersion.HasValue)
            {
                try
                {
                    await _store.MarkFailedAsync(
                        tenantId,
                        scope,
                        key,
                        claim.OwnerToken.Value,
                        claim.ConcurrencyVersion.Value,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception markEx)
                {
                    LogMarkFailedError(_logger, markEx, key.Value);
                }
            }

            throw;
        }
        finally
        {
            _contextAccessor.IdempotencyContext = null;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Idempotency lease was lost before completion for key {Key} in scope {Scope}")]
    private static partial void LogLeaseLost(ILogger logger, string key, string scope);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Failed to mark idempotency record as Failed for key {Key}")]
    private static partial void LogMarkFailedError(ILogger logger, Exception exception, string key);
}
