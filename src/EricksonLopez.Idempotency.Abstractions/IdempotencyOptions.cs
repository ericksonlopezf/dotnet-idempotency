// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Idempotency;

/// <summary>
/// Encapsulates configuration options for the idempotency infrastructure.
/// </summary>
public sealed class IdempotencyOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether idempotency enforcement is globally active.
    /// </summary>
    /// <remarks>
    /// When <see langword="false"/>, all idempotency middleware and endpoint filters pass through
    /// without inspecting or persisting any state. This flag allows idempotency to be disabled
    /// globally (e.g., for feature flagging or local development) without removing the middleware
    /// or filter registrations from the pipeline.
    /// Defaults to <see langword="true"/>.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether an idempotency key header is mandatory on guarded endpoints.
    /// </summary>
    public bool RequireIdempotencyKey { get; set; }

    /// <summary>
    /// Gets or sets the default duration for which an acquired execution lease remains valid before another worker may steal it.
    /// </summary>
    public TimeSpan DefaultLeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the default duration for which completed idempotency records are retained before becoming eligible for cleanup.
    /// </summary>
    public TimeSpan DefaultRetentionDuration { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets or sets the maximum request payload size in bytes that will be read and buffered for fingerprint computation.
    /// </summary>
    /// <remarks>
    /// When the request body exceeds this limit, only the first <c>MaxRequestBodySizeBytes</c> bytes
    /// are read and used for fingerprint computation; the remainder is discarded for hashing purposes
    /// but the full body is still forwarded to the downstream handler.
    /// Defaults to 1 MiB (1 048 576 bytes).
    /// </remarks>
    public long MaxRequestBodySizeBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// Gets or sets the header name used to supply the idempotency key.
    /// </summary>
    public string HeaderName { get; set; } = "Idempotency-Key";

    /// <summary>
    /// Gets or sets a value indicating whether response bodies are included in the cached idempotency record.
    /// </summary>
    public bool StoreResponseBody { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether only successful responses (HTTP 2xx) are cached.
    /// When <see langword="true"/> (the default), error responses (4xx, 5xx) are not stored and the
    /// client may retry with the same idempotency key to obtain a fresh execution.
    /// When <see langword="false"/>, all responses including error responses are cached and replayed.
    /// </summary>
    /// <remarks>
    /// Setting this to <see langword="true"/> is strongly recommended for APIs where transient failures
    /// (e.g., upstream timeouts, validation errors) should be retryable without needing a new key.
    /// </remarks>
    public bool CacheOnlySuccessResponses { get; set; } = true;

    /// <summary>
    /// Gets or sets a delegate that extracts the tenant identifier from the ambient execution context.
    /// </summary>
    /// <remarks>
    /// When <see langword="null"/>, the infrastructure falls back to well-known conventions
    /// such as <c>HttpContext.Items["TenantId"]</c> or the <c>tenant_id</c> JWT claim.
    /// The delegate receives the raw context object (e.g., <c>HttpContext</c>)
    /// and must return the resolved <see cref="Guid"/> tenant identifier.
    /// </remarks>
    public Func<object, Guid>? TenantIdExtractor { get; set; }
}
