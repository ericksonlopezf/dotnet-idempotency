// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace EricksonLopez.Idempotency.AspNetCore;

/// <summary>
/// Provides ASP.NET Core middleware that intercepts incoming HTTP requests to enforce idempotency semantics.
/// </summary>
public sealed class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyMiddleware"/> class with the specified next request delegate.
    /// </summary>
    /// <param name="next">The next middleware delegate in the HTTP execution pipeline.</param>
    /// <exception cref="ArgumentNullException"><paramref name="next"/> is <see langword="null"/></exception>
    public IdempotencyMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    /// <summary>
    /// Intercepts and processes the HTTP request to ensure idempotent execution.
    /// </summary>
    /// <param name="context">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="store">The persistence store for recording idempotency state.</param>
    /// <param name="options">The idempotency configuration options.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context, IIdempotencyStore store, IdempotencyOptions options)
    {
        // F-001: global kill-switch — pass through without any idempotency enforcement.
        if (!options.Enabled)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var endpoint = context.GetEndpoint();
        var idempotentAttr = endpoint?.Metadata.GetMetadata<IdempotentAttribute>();
        var hasHeader = context.Request.Headers.TryGetValue(options.HeaderName, out var rawKey) && !string.IsNullOrWhiteSpace(rawKey);

        if (idempotentAttr is null && !options.RequireIdempotencyKey && !hasHeader)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Check [Idempotent(Enabled = false)] on the endpoint — skip enforcement if disabled
        if (idempotentAttr is { Enabled: false })
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!hasHeader)
        {
            if (idempotentAttr?.Required == true || options.RequireIdempotencyKey)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/problem+json";
                var problem = new IdempotencyProblemDetails(
                    "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                    "Missing Idempotency Key",
                    400,
                    $"The '{options.HeaderName}' request header is mandatory for this operation.");
                var bytes = JsonSerializer.SerializeToUtf8Bytes(problem, IdempotencyJsonContext.Default.IdempotencyProblemDetails);
                await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
                return;
            }

            await _next(context).ConfigureAwait(false);
            return;
        }

        var key = new IdempotencyKey(rawKey.ToString());
        var tenantId = ExtractTenantId(context, options);
        var scope = idempotentAttr?.Scope ?? (string.IsNullOrEmpty(context.Request.Path.Value) ? "/" : context.Request.Path.Value);
        var subject = context.User?.FindFirst("sub")?.Value;

        context.Request.EnableBuffering();
        using var bodyMemory = new MemoryStream();

        // F-006: Respect MaxRequestBodySizeBytes — read up to the limit for fingerprinting,
        // then reset the body so the downstream handler receives the full stream.
        var maxBytes = options.MaxRequestBodySizeBytes;
        if (maxBytes > 0)
        {
            var buffer = new byte[Math.Min(maxBytes, context.Request.Body.CanSeek ? context.Request.ContentLength ?? maxBytes : maxBytes)];
            int totalRead = 0;
            int bytesRead;
            while (totalRead < buffer.Length &&
                   (bytesRead = await context.Request.Body.ReadAsync(buffer.AsMemory(totalRead, (int)(buffer.Length - totalRead)), context.RequestAborted).ConfigureAwait(false)) > 0)
            {
                totalRead += bytesRead;
            }
            await bodyMemory.WriteAsync(buffer.AsMemory(0, totalRead), context.RequestAborted).ConfigureAwait(false);
        }

        context.Request.Body.Position = 0;

        var fingerprint = IdempotencyFingerprintHasher.Compute(
            context.Request.Method,
            scope,
            tenantId.ToString(),
            subject,
            bodyMemory.ToArray());

        var leaseDuration = idempotentAttr != null ? TimeSpan.FromSeconds(idempotentAttr.LeaseDurationSeconds) : options.DefaultLeaseDuration;
        var retentionDuration = idempotentAttr != null ? TimeSpan.FromDays(idempotentAttr.RetentionDurationDays) : options.DefaultRetentionDuration;

        var claim = await store.TryAcquireAsync(
            tenantId,
            scope,
            key,
            fingerprint,
            leaseDuration,
            retentionDuration,
            context.RequestAborted).ConfigureAwait(false);

        if (claim.Status == ClaimResultStatus.FingerprintMismatch)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/problem+json";
            var problem = new IdempotencyProblemDetails(
                "https://tools.ietf.org/html/rfc9110#section-15.5.10",
                "Idempotency Key Conflict",
                409,
                "Idempotency key mismatch: a previous request used the same key with different payload parameters.");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(problem, IdempotencyJsonContext.Default.IdempotencyProblemDetails);
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        if (claim.Status == ClaimResultStatus.InFlightConflict)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.Headers.RetryAfter = "2";
            context.Response.ContentType = "application/problem+json";
            var problem = new IdempotencyProblemDetails(
                "https://tools.ietf.org/html/rfc9110#section-15.5.10",
                "Operation In Flight",
                409,
                "A concurrent request with the same idempotency key is currently processing.");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(problem, IdempotencyJsonContext.Default.IdempotencyProblemDetails);
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        if (claim.Status == ClaimResultStatus.CompletedReplay)
        {
            if (claim.CachedResponse != null)
            {
                context.Response.Headers["X-Idempotency-Replayed"] = "true";
                foreach (var (hKey, hValues) in claim.CachedResponse.Headers)
                {
                    context.Response.Headers[hKey] = hValues;
                }

                context.Response.StatusCode = claim.CachedResponse.StatusCode;
                await context.Response.Body.WriteAsync(claim.CachedResponse.Body, context.RequestAborted).ConfigureAwait(false);
            }
            return;
        }

        var originalBodyStream = context.Response.Body;
        using var capturedStream = new MemoryStream();
        context.Response.Body = capturedStream;

        try
        {
            await _next(context).ConfigureAwait(false);

            capturedStream.Position = 0;
            var responseBytes = capturedStream.ToArray();
            capturedStream.Position = 0;
            await capturedStream.CopyToAsync(originalBodyStream, context.RequestAborted).ConfigureAwait(false);

            var statusCode = context.Response.StatusCode;
            var isSuccess = statusCode >= 200 && statusCode < 300;

            // When CacheOnlySuccessResponses is true, only persist 2xx responses.
            // Non-2xx responses are marked as failed so the client can retry with the same key.
            if (options.CacheOnlySuccessResponses && !isSuccess)
            {
                if (claim.OwnerToken.HasValue && claim.ConcurrencyVersion.HasValue)
                {
                    await store.MarkFailedAsync(
                        tenantId,
                        scope,
                        key,
                        claim.OwnerToken.Value,
                        claim.ConcurrencyVersion.Value,
                        CancellationToken.None).ConfigureAwait(false);
                }
                return;
            }

            var headersToPersist = context.Response.Headers
                .Where(h => !h.Key.StartsWith(':') && !h.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(h => h.Key, h => h.Value.Select(v => v ?? string.Empty).ToArray());

            await store.MarkCompletedAsync(
                tenantId,
                scope,
                key,
                claim.OwnerToken!.Value,
                claim.ConcurrencyVersion!.Value,
                statusCode,
                headersToPersist,
                responseBytes,
                retentionDuration,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            if (claim.OwnerToken.HasValue && claim.ConcurrencyVersion.HasValue)
            {
                await store.MarkFailedAsync(
                    tenantId,
                    scope,
                    key,
                    claim.OwnerToken.Value,
                    claim.ConcurrencyVersion.Value,
                    CancellationToken.None).ConfigureAwait(false);
            }
            throw;
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }

    /// <summary>
    /// Extracts the tenant identifier from the current HTTP context using the configured extractor delegate.
    /// Falls back to <c>HttpContext.Items["TenantId"]</c>, then to the <c>tenant_id</c> JWT claim,
    /// and finally returns <see cref="Guid.Empty"/> if no tenant can be resolved.
    /// </summary>
    internal static Guid ExtractTenantId(HttpContext httpContext, IdempotencyOptions options)
    {
        // 1. Use custom extractor if provided
        if (options.TenantIdExtractor is { } extractor)
            return extractor(httpContext);

        // 2. Check HttpContext.Items["TenantId"] (populated by EricksonLopez.MultiTenancy middleware)
        if (httpContext.Items.TryGetValue("TenantId", out var item) && item is Guid tenantGuid)
            return tenantGuid;

        // 3. Fall back to JWT claim "tenant_id"
        var tenantClaim = httpContext.User?.FindFirst("tenant_id")?.Value;
        if (tenantClaim is not null && Guid.TryParse(tenantClaim, out var claimGuid))
            return claimGuid;

        return Guid.Empty;
    }
}
