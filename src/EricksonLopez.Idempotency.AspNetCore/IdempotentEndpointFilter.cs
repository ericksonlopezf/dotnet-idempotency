// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace EricksonLopez.Idempotency.AspNetCore;

/// <summary>
/// Provides an endpoint filter that guards ASP.NET Core Minimal API endpoints with idempotency guarantees.
/// </summary>
public sealed class IdempotentEndpointFilter : IEndpointFilter
{
    private readonly IIdempotencyStore _store;
    private readonly IdempotencyOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotentEndpointFilter"/> class with the specified store and options.
    /// </summary>
    /// <param name="store">The persistence store for recording idempotency state.</param>
    /// <param name="options">The idempotency configuration options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="options"/> is <see langword="null"/></exception>
    public IdempotentEndpointFilter(IIdempotencyStore store, IdempotencyOptions options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // F-001: global kill-switch — pass through without any idempotency enforcement.
        if (!_options.Enabled)
        {
            return await next(context).ConfigureAwait(false);
        }

        if (!httpContext.Request.Headers.TryGetValue(_options.HeaderName, out var rawKey) ||
            string.IsNullOrWhiteSpace(rawKey))
        {
            if (_options.RequireIdempotencyKey)
            {
                return Results.Problem(
                    detail: $"The '{_options.HeaderName}' request header is mandatory for this endpoint.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Missing Idempotency Key");
            }

            return await next(context).ConfigureAwait(false);
        }

        var key = new IdempotencyKey(rawKey.ToString());
        var tenantId = IdempotencyMiddleware.ExtractTenantId(httpContext, _options);
        var scope = string.IsNullOrEmpty(httpContext.Request.Path.Value) ? "/" : httpContext.Request.Path.Value;
        var subject = httpContext.User?.FindFirst("sub")?.Value;

        // Buffer request body for fingerprinting
        httpContext.Request.EnableBuffering();
        using var bodyMemory = new MemoryStream();

        // F-006: Respect MaxRequestBodySizeBytes — read up to the limit for fingerprinting.
        var maxBytes = _options.MaxRequestBodySizeBytes;
        if (maxBytes > 0)
        {
            var buffer = new byte[Math.Min(maxBytes, httpContext.Request.Body.CanSeek ? httpContext.Request.ContentLength ?? maxBytes : maxBytes)];
            int totalRead = 0;
            int bytesRead;
            while (totalRead < buffer.Length &&
                   (bytesRead = await httpContext.Request.Body.ReadAsync(buffer.AsMemory(totalRead, (int)(buffer.Length - totalRead)), httpContext.RequestAborted).ConfigureAwait(false)) > 0)
            {
                totalRead += bytesRead;
            }
            await bodyMemory.WriteAsync(buffer.AsMemory(0, totalRead), httpContext.RequestAborted).ConfigureAwait(false);
        }

        httpContext.Request.Body.Position = 0;

        var fingerprint = IdempotencyFingerprintHasher.Compute(
            httpContext.Request.Method,
            scope,
            tenantId.ToString(),
            subject,
            bodyMemory.ToArray());

        var claimResult = await _store.TryAcquireAsync(
            tenantId,
            scope,
            key,
            fingerprint,
            _options.DefaultLeaseDuration,
            _options.DefaultRetentionDuration,
            httpContext.RequestAborted).ConfigureAwait(false);

        if (claimResult.Status == ClaimResultStatus.FingerprintMismatch)
        {
            return Results.Problem(
                detail: "Idempotency key mismatch: a previous request used the same key with different payload parameters.",
                statusCode: StatusCodes.Status409Conflict,
                title: "Idempotency Key Conflict");
        }

        if (claimResult.Status == ClaimResultStatus.InFlightConflict)
        {
            httpContext.Response.Headers.RetryAfter = "2";
            return Results.Problem(
                detail: "A concurrent request with the same idempotency key is currently processing.",
                statusCode: StatusCodes.Status409Conflict,
                title: "Operation In Flight");
        }

        if (claimResult.Status == ClaimResultStatus.CompletedReplay)
        {
            if (claimResult.CachedResponse is not null)
            {
                httpContext.Response.Headers["X-Idempotency-Replayed"] = "true";
                foreach (var (headerKey, headerValues) in claimResult.CachedResponse.Headers)
                {
                    httpContext.Response.Headers[headerKey] = headerValues;
                }

                httpContext.Response.StatusCode = claimResult.CachedResponse.StatusCode;
                await httpContext.Response.Body.WriteAsync(claimResult.CachedResponse.Body, httpContext.RequestAborted).ConfigureAwait(false);
            }
            return Results.Empty;
        }

        // Execute original endpoint and capture response
        var originalBodyStream = httpContext.Response.Body;
        using var capturedStream = new MemoryStream();
        httpContext.Response.Body = capturedStream;

        try
        {
            var result = await next(context).ConfigureAwait(false);

            capturedStream.Position = 0;
            var responseBytes = capturedStream.ToArray();
            capturedStream.Position = 0;
            await capturedStream.CopyToAsync(originalBodyStream, httpContext.RequestAborted).ConfigureAwait(false);

            var statusCode = httpContext.Response.StatusCode;
            var isSuccess = statusCode >= 200 && statusCode < 300;

            // When CacheOnlySuccessResponses is true, only persist 2xx responses.
            // Non-2xx responses are marked as failed so the client can retry with the same key.
            if (_options.CacheOnlySuccessResponses && !isSuccess)
            {
                if (claimResult.OwnerToken.HasValue && claimResult.ConcurrencyVersion.HasValue)
                {
                    await _store.MarkFailedAsync(
                        tenantId,
                        scope,
                        key,
                        claimResult.OwnerToken.Value,
                        claimResult.ConcurrencyVersion.Value,
                        CancellationToken.None).ConfigureAwait(false);
                }
                return result;
            }

            var headersToPersist = httpContext.Response.Headers
                .Where(h => !h.Key.StartsWith(':') && !h.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(h => h.Key, h => h.Value.Select(v => v ?? string.Empty).ToArray());

            await _store.MarkCompletedAsync(
                tenantId,
                scope,
                key,
                claimResult.OwnerToken!.Value,
                claimResult.ConcurrencyVersion!.Value,
                statusCode,
                headersToPersist,
                responseBytes,
                _options.DefaultRetentionDuration,
                CancellationToken.None).ConfigureAwait(false);

            return result;
        }
        catch (Exception)
        {
            if (claimResult.OwnerToken.HasValue && claimResult.ConcurrencyVersion.HasValue)
            {
                await _store.MarkFailedAsync(
                    tenantId,
                    scope,
                    key,
                    claimResult.OwnerToken.Value,
                    claimResult.ConcurrencyVersion.Value,
                    CancellationToken.None).ConfigureAwait(false);
            }
            throw;
        }
        finally
        {
            httpContext.Response.Body = originalBodyStream;
        }
    }
}
