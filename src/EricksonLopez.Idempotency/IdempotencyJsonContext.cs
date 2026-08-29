// Copyright © Erickson Lopez. MIT License.
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EricksonLopez.Idempotency;

/// <summary>
/// Provides source-generated JSON serialization metadata for Native AOT compatibility.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dictionary<string, string[]>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string[]>))]
[JsonSerializable(typeof(CachedIdempotencyResponse))]
[JsonSerializable(typeof(IdempotencyStatus))]
[JsonSerializable(typeof(ClaimResultStatus))]
[JsonSerializable(typeof(IdempotencyProblemDetails))]
public sealed partial class IdempotencyJsonContext : JsonSerializerContext
{
}
