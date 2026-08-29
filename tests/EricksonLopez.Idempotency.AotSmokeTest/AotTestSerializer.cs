// Copyright © Erickson Lopez. MIT License.
using System;
using System.Text.Json;

namespace EricksonLopez.Idempotency.AotSmokeTest;

public sealed class AotTestSerializer : IIdempotencySerializer
{
    public byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, typeof(T), AotTestJsonContext.Default);
    public T? Deserialize<T>(ReadOnlyMemory<byte> bytes) => (T?)JsonSerializer.Deserialize(bytes.Span, typeof(T), AotTestJsonContext.Default);
}
