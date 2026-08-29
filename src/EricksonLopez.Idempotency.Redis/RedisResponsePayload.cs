// Copyright © Erickson Lopez. MIT License.
using System.Collections.Generic;

namespace EricksonLopez.Idempotency.Redis;

/// <summary>
/// Internal payload model for serializing response data in Redis.
/// </summary>
internal sealed class RedisResponsePayload
{
    public int StatusCode { get; set; }
    public IReadOnlyDictionary<string, string[]> Headers { get; set; } = new Dictionary<string, string[]>();
    public byte[] Body { get; set; } = [];

    public RedisResponsePayload() { }

    public RedisResponsePayload(int statusCode, IReadOnlyDictionary<string, string[]> headers, byte[] body)
    {
        StatusCode = statusCode;
        Headers = headers;
        Body = body;
    }
}
