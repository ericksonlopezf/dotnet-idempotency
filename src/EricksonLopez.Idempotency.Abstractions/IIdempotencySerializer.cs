// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Idempotency;

/// <summary>
/// Defines a contract for serializing and deserializing response and request payloads for idempotency storage.
/// </summary>
public interface IIdempotencySerializer
{
    /// <summary>
    /// Serializes an object value into a raw UTF-8 byte array.
    /// </summary>
    /// <typeparam name="T">The type of the value to serialize.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>A byte array containing the serialized representation.</returns>
    byte[] Serialize<T>(T value);

    /// <summary>
    /// Deserializes a raw byte payload into an instance of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The target deserialization type.</typeparam>
    /// <param name="bytes">The memory buffer containing the serialized payload bytes.</param>
    /// <returns>The deserialized object instance, or <see langword="null"/> if deserialization produces no value.</returns>
    T? Deserialize<T>(ReadOnlyMemory<byte> bytes);
}
