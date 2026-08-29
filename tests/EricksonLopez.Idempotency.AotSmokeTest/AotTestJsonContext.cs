// Copyright © Erickson Lopez. MIT License.
using System.Text.Json.Serialization;

namespace EricksonLopez.Idempotency.AotSmokeTest;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OrderModel))]
public sealed partial class AotTestJsonContext : JsonSerializerContext
{
}
