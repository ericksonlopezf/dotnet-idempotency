// Copyright © Erickson Lopez. MIT License.
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Idempotency;
using EricksonLopez.Idempotency.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace EricksonLopez.Idempotency.Showcase.Levels;

/// <summary>
/// Demonstrates custom implementations of all public extension points:
/// IIdempotencyPolicy, IIdempotencySerializer (both constructors),
/// IIdempotencyFingerprintGenerator (both static Compute and instance GenerateFingerprint),
/// IIdempotencyContextAccessor ambient context reading, and IIdempotencyKeyProvider{TContext}.
/// </summary>
public sealed class Level8Customization : ILevel
{
    /// <inheritdoc/>
    public string Name => "Level 8 — Custom Policies, Serializers, Fingerprinters & Context Providers";

    /// <inheritdoc/>
    public string Description => "Extending the framework via IIdempotencyPolicy, IIdempotencySerializer (both ctors), IIdempotencyFingerprintGenerator, IIdempotencyContextAccessor, and IIdempotencyKeyProvider<TContext>.";

    /// <inheritdoc/>
    public async Task ExecuteAsync()
    {
        // ─── 1. Custom IIdempotencyPolicy ─────────────────────────────────────────
        Console.WriteLine("1. Custom IIdempotencyPolicy — StrictPaymentPolicy:");
        var customPolicy = new StrictPaymentPolicy();
        Console.WriteLine($" -> LeaseDuration:      {customPolicy.LeaseDuration.TotalSeconds}s (default 30s)");
        Console.WriteLine($" -> RetentionDuration:  {customPolicy.RetentionDuration.TotalDays} days (default 7 days)");
        Console.WriteLine($" -> AllowRetryOnFailure:{customPolicy.AllowRetryOnFailure} (strict: no retry after failure)");
        Console.WriteLine($" -> Is 200 Cacheable?   {customPolicy.IsCacheableStatusCode(200)}");
        Console.WriteLine($" -> Is 201 Cacheable?   {customPolicy.IsCacheableStatusCode(201)}");
        Console.WriteLine($" -> Is 202 Cacheable?   {customPolicy.IsCacheableStatusCode(202)} (strict: only 200/201)");
        Console.WriteLine($" -> Is 400 Cacheable?   {customPolicy.IsCacheableStatusCode(400)}");

        // ─── 2. Custom IIdempotencySerializer — PrettyJsonSerializer ─────────────
        Console.WriteLine("\n2. Custom IIdempotencySerializer — PrettyJsonSerializer:");
        var prettySerializer = new PrettyJsonSerializer();
        var sampleData = new CustomSampleData("Custom-ID-1", DateTimeOffset.UtcNow);
        var prettyBytes = prettySerializer.Serialize(sampleData);
        var prettyDeserialized = prettySerializer.Deserialize<CustomSampleData>(prettyBytes);
        Console.WriteLine($" -> Serialized (indented) size: {prettyBytes.Length} bytes");
        Console.WriteLine($" -> Deserialized ID:           '{prettyDeserialized?.Id}'");

        // ─── 3. SystemTextJsonIdempotencySerializer — custom JsonSerializerOptions ctor ─
        Console.WriteLine("\n3. SystemTextJsonIdempotencySerializer(JsonSerializerOptions) — custom options ctor:");
        var customJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var customJsonSerializer = new SystemTextJsonIdempotencySerializer(customJsonOptions);
        var compactBytes = customJsonSerializer.Serialize(sampleData);
        var compactDeserialized = customJsonSerializer.Deserialize<CustomSampleData>(compactBytes);
        Console.WriteLine($" -> Compact camelCase serialized size: {compactBytes.Length} bytes");
        Console.WriteLine($" -> Deserialized ID:                  '{compactDeserialized?.Id}'");

        // ─── 4. Custom IIdempotencyFingerprintGenerator (both overloads) ──────────
        Console.WriteLine("\n4. Custom IIdempotencyFingerprintGenerator — MethodScopeFingerprintGenerator:");
        var customGenerator = new MethodScopeFingerprintGenerator();

        // instance method GenerateFingerprint (implements the interface)
        var instanceFp = customGenerator.GenerateFingerprint("POST", "orders", "tenant-100", "user-1", ReadOnlySpan<byte>.Empty);
        Console.WriteLine($" -> [instance] GenerateFingerprint: {instanceFp[..16]}...");

        // static method Compute (direct usage without interface)
        var staticFp = IdempotencyFingerprintHasher.Compute("POST", "orders", "tenant-100", "user-1", ReadOnlySpan<byte>.Empty);
        Console.WriteLine($" -> [static]   IdempotencyFingerprintHasher.Compute: {staticFp[..16]}...");

        Console.WriteLine($" -> Fingerprints differ? {!string.Equals(instanceFp, staticFp, StringComparison.Ordinal)} (custom vs default algorithm)");

        // ─── 5. IIdempotencyContextAccessor — reading ambient context ─────────────
        Console.WriteLine("\n5. IIdempotencyContextAccessor — reading ambient IdempotencyContext during execution:");

        var store = new InMemoryIdempotencyStore();
        var contextAccessor = new AsyncLocalIdempotencyContextAccessor();
        var engine = new IdempotencyEngine(
            store, customPolicy, prettySerializer,
            contextAccessor, NullLogger<IdempotencyEngine>.Instance);

        var ambientKey = new IdempotencyKey("AMBIENT-KEY-001");
        var ambientFp = IdempotencyFingerprintHasher.Compute("POST", "orders", Guid.Empty.ToString(), null, System.Text.Encoding.UTF8.GetBytes("{\"item\":\"X\"}"));

        IdempotencyContext? capturedContext = null;

        await engine.ExecuteAsync(Guid.Empty, "orders", ambientKey, ambientFp, ct =>
        {
            // Read ambient context from inside the operation
            capturedContext = contextAccessor.IdempotencyContext;
            return Task.FromResult(new CustomSampleData("RESULT-001", DateTimeOffset.UtcNow));
        });

        Console.WriteLine($" -> Ambient TenantId:           {capturedContext?.TenantId}");
        Console.WriteLine($" -> Ambient Key:                '{capturedContext?.Key}'");
        Console.WriteLine($" -> Ambient Scope:              '{capturedContext?.Scope}'");
        Console.WriteLine($" -> Ambient IsReplay:           {capturedContext?.IsReplay}");
        Console.WriteLine($" -> Ambient OwnerToken:         {capturedContext?.OwnerToken?.ToString()[..8]}...");
        Console.WriteLine($" -> Ambient ConcurrencyVersion: {capturedContext?.ConcurrencyVersion}");

        // After execution, context is cleared (null)
        Console.WriteLine($" -> After execution context:    {(contextAccessor.IdempotencyContext is null ? "null (cleared)" : "still set")}");

        // ─── 6. Custom IIdempotencyKeyProvider<TContext> ──────────────────────────
        Console.WriteLine("\n6. Custom IIdempotencyKeyProvider<TContext> — resolving keys from arbitrary contexts:");

        var keyProvider = new HeaderDictionaryKeyProvider();

        // Simulate a headers dictionary (e.g., from a messaging system or gRPC metadata)
        var headers = new System.Collections.Generic.Dictionary<string, string>
        {
            ["idempotency-key"] = "HEADER-KEY-VALUE-001",
            ["correlation-id"] = "CORR-9999"
        };

        var resolvedKey = await keyProvider.TryGetKeyAsync(headers, CancellationToken.None);
        Console.WriteLine($" -> Resolved from headers: '{resolvedKey}'");

        var emptyHeaders = new System.Collections.Generic.Dictionary<string, string>();
        var notFound = await keyProvider.TryGetKeyAsync(emptyHeaders, CancellationToken.None);
        Console.WriteLine($" -> Not found (empty headers): {(notFound is null ? "null (no key header)" : notFound.ToString())}");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n[SUCCESS] All custom extension points demonstrated.");
        Console.ResetColor();
    }

    /// <summary>
    /// Represents a custom fingerprint generator that hashes only operation name, scope, and tenant
    /// (ignores authenticated subject and payload bytes for demonstration).
    /// </summary>
    public sealed class MethodScopeFingerprintGenerator : IIdempotencyFingerprintGenerator
    {
        /// <inheritdoc/>
        public string GenerateFingerprint(
            string operationName,
            string scope,
            string tenantId,
            string? authenticatedSubject,
            ReadOnlySpan<byte> payloadBytes)
        {
            var raw = $"{operationName}:{scope}:{tenantId}:{authenticatedSubject ?? string.Empty}";
            var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(hash);
        }
    }

    /// <summary>
    /// Represents a strict idempotency evaluation policy for financial payment operations.
    /// Caches only 200 and 201 responses; disallows retry after failure.
    /// </summary>
    public sealed class StrictPaymentPolicy : IIdempotencyPolicy
    {
        /// <inheritdoc/>
        public TimeSpan LeaseDuration => TimeSpan.FromMinutes(2);

        /// <inheritdoc/>
        public TimeSpan RetentionDuration => TimeSpan.FromDays(30);

        /// <inheritdoc/>
        public bool AllowRetryOnFailure => false;

        /// <inheritdoc/>
        public bool IsCacheableStatusCode(int statusCode) => statusCode is 200 or 201;
    }

    /// <summary>
    /// Represents a custom indented JSON serializer for debugging and local development.
    /// </summary>
    public sealed class PrettyJsonSerializer : IIdempotencySerializer
    {
        private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };

        /// <inheritdoc/>
        public byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, _options);

        /// <inheritdoc/>
        public T? Deserialize<T>(ReadOnlyMemory<byte> bytes) => JsonSerializer.Deserialize<T>(bytes.Span, _options);
    }

    /// <summary>
    /// Represents a custom key provider that resolves idempotency keys from a string header dictionary
    /// (e.g., from gRPC metadata, messaging system headers, or custom HTTP parsers).
    /// </summary>
    public sealed class HeaderDictionaryKeyProvider : IIdempotencyKeyProvider<System.Collections.Generic.Dictionary<string, string>>
    {
        /// <inheritdoc/>
        public ValueTask<IdempotencyKey?> TryGetKeyAsync(
            System.Collections.Generic.Dictionary<string, string> context,
            CancellationToken cancellationToken = default)
        {
            if (context.TryGetValue("idempotency-key", out var headerValue) &&
                IdempotencyKey.TryParse(headerValue, out var key))
            {
                return ValueTask.FromResult<IdempotencyKey?>(key);
            }

            return ValueTask.FromResult<IdempotencyKey?>(null);
        }
    }

    /// <summary>
    /// Represents a sample custom data model.
    /// </summary>
    /// <param name="Id">The sample identifier.</param>
    /// <param name="Timestamp">The sample timestamp.</param>
    public sealed record CustomSampleData(string Id, DateTimeOffset Timestamp);
}
