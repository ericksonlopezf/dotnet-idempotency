// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Idempotency.Exceptions;
using EricksonLopez.Mediator;

namespace EricksonLopez.Idempotency.Mediator;

/// <summary>
/// Provides a pipeline behavior for <see cref="EricksonLopez.Mediator"/> that enforces idempotency semantics
/// on commands implementing <see cref="IIdempotentRequest"/>.
/// </summary>
/// <typeparam name="TRequest">The type of request being handled. Must implement <see cref="IIdempotentRequest"/>.</typeparam>
/// <typeparam name="TResponse">The type of response yielded by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// The behavior uses the <see cref="IIdempotentRequest.TenantId"/> property to partition idempotency records
/// by tenant. In multi-tenant systems, each command implementation must return the correct tenant identifier.
/// </para>
/// <para>
/// The scope is automatically derived from the fully-qualified type name of <typeparamref name="TRequest"/>,
/// ensuring that idempotency keys are isolated per command type even if the same key string is reused
/// across different command types.
/// </para>
/// </remarks>
public sealed class IdempotencyPipelineBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IIdempotentRequest
{
    private readonly IIdempotencyStore _store;
    private readonly IIdempotencySerializer _serializer;
    private readonly IIdempotencyPolicy _policy;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyPipelineBehavior{TRequest, TResponse}"/> class with the specified store, serializer, and policy.
    /// </summary>
    /// <param name="store">The persistence store for recording idempotency state.</param>
    /// <param name="serializer">The serializer used for response caching.</param>
    /// <param name="policy">The evaluation policy determining lease and retention rules.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/>, <paramref name="serializer"/>, or <paramref name="policy"/> is <see langword="null"/></exception>
    public IdempotencyPipelineBehavior(
        IIdempotencyStore store,
        IIdempotencySerializer serializer,
        IIdempotencyPolicy policy)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <inheritdoc />
    public async ValueTask<TResponse> Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, INext<TResponse>
    {
        // ISSUE-002 fix: read TenantId from the request itself (not hardcoded to Guid.Empty).
        // This is the correct way for multi-tenant CQRS: each command carries its tenant context.
        var tenantId = request.TenantId;
        var scope = typeof(TRequest).FullName ?? typeof(TRequest).Name;
        var payloadBytes = _serializer.Serialize(request);
        var fingerprint = IdempotencyFingerprintHasher.Compute(typeof(TRequest).Name, scope, tenantId.ToString(), null, payloadBytes);

        var claim = await _store.TryAcquireAsync(
            tenantId,
            scope,
            request.IdempotencyKey,
            fingerprint,
            _policy.LeaseDuration,
            _policy.RetentionDuration,
            cancellationToken).ConfigureAwait(false);

        if (claim.Status == ClaimResultStatus.FingerprintMismatch)
        {
            throw new IdempotencyFingerprintMismatchException(request.IdempotencyKey.Value, claim.ExistingFingerprint, fingerprint);
        }

        if (claim.Status == ClaimResultStatus.InFlightConflict)
        {
            throw new IdempotencyConflictException(request.IdempotencyKey.Value);
        }

        if (claim.Status == ClaimResultStatus.CompletedReplay)
        {
            if (claim.CachedResponse != null)
            {
                var cachedObject = _serializer.Deserialize<TResponse>(claim.CachedResponse.Body);
                return cachedObject!;
            }

            return default!;
        }

        try
        {
            var response = await next.InvokeAsync().ConfigureAwait(false);
            var responseBytes = _serializer.Serialize(response);

            await _store.MarkCompletedAsync(
                tenantId,
                scope,
                request.IdempotencyKey,
                claim.OwnerToken!.Value,
                claim.ConcurrencyVersion!.Value,
                200,
                new Dictionary<string, string[]>(),
                responseBytes,
                _policy.RetentionDuration,
                CancellationToken.None).ConfigureAwait(false);

            return response;
        }
        catch (Exception)
        {
            if (claim.OwnerToken.HasValue && claim.ConcurrencyVersion.HasValue)
            {
                await _store.MarkFailedAsync(
                    tenantId,
                    scope,
                    request.IdempotencyKey,
                    claim.OwnerToken.Value,
                    claim.ConcurrencyVersion.Value,
                    CancellationToken.None).ConfigureAwait(false);
            }
            throw;
        }
    }
}
