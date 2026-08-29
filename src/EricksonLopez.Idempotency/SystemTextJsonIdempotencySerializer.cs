// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace EricksonLopez.Idempotency;

/// <summary>
/// Provides a Native AOT compatible implementation of <see cref="IIdempotencySerializer"/> using <see cref="System.Text.Json"/>.
/// </summary>
public sealed class SystemTextJsonIdempotencySerializer : IIdempotencySerializer
{
    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemTextJsonIdempotencySerializer"/> class using default serializer options.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026:MembersAnnotatedWithRequiresUnreferencedCode", Justification = "Fallback resolver is combined with source generated resolver for non-AOT scenarios.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "Fallback resolver is combined with source generated resolver for non-AOT scenarios.")]
    public SystemTextJsonIdempotencySerializer()
    {
        _options = new JsonSerializerOptions
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(IdempotencyJsonContext.Default, new DefaultJsonTypeInfoResolver()),
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemTextJsonIdempotencySerializer"/> class with custom options.
    /// </summary>
    /// <param name="options">The custom serializer options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/></exception>
    public SystemTextJsonIdempotencySerializer(JsonSerializerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    [UnconditionalSuppressMessage("Trimming", "IL2026:MembersAnnotatedWithRequiresUnreferencedCode", Justification = "AOT source-generated TypeInfoResolver is combined with DefaultJsonTypeInfoResolver.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "AOT source-generated TypeInfoResolver is combined with DefaultJsonTypeInfoResolver.")]
    public byte[] Serialize<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, _options);
    }

    /// <inheritdoc />
    [UnconditionalSuppressMessage("Trimming", "IL2026:MembersAnnotatedWithRequiresUnreferencedCode", Justification = "AOT source-generated TypeInfoResolver is combined with DefaultJsonTypeInfoResolver.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "AOT source-generated TypeInfoResolver is combined with DefaultJsonTypeInfoResolver.")]
    public T? Deserialize<T>(ReadOnlyMemory<byte> bytes)
    {
        return JsonSerializer.Deserialize<T>(bytes.Span, _options);
    }
}
