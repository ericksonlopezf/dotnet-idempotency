// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;

namespace EricksonLopez.Idempotency;

/// <summary>
/// Represents the cached HTTP or application response associated with a completed idempotent operation.
/// </summary>
/// <param name="StatusCode">The HTTP or logical status code of the response.</param>
/// <param name="Headers">The headers associated with the response.</param>
/// <param name="Body">The serialized raw body payload.</param>
public sealed record CachedIdempotencyResponse(
    int StatusCode,
    IReadOnlyDictionary<string, string[]> Headers,
    ReadOnlyMemory<byte> Body);
