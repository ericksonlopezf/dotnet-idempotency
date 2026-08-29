// Copyright © Erickson Lopez. MIT License.
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EricksonLopez.Idempotency.Redis;

/// <summary>
/// Source-generated JSON context for the Redis idempotency store — ensures AOT compatibility.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RedisResponsePayload))]
[JsonSerializable(typeof(Dictionary<string, string[]>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string[]>))]
internal sealed partial class RedisIdempotencyJsonContext : JsonSerializerContext
{
}
