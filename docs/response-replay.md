# Response Capturing & Replay Mechanics

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Response Replay Fundamentals

When an operation with an existing completed key is received, the system bypasses the business handler and replays the cached output.

To ensure client fidelity, the cached response captures:
1. **HTTP Status Code**: (e.g. `200 OK`, `201 Created`, `400 Bad Request`).
2. **Response Headers**: (e.g. `Content-Type`, `Location`, `ETag`).
3. **Response Body**: Serialized bytes stored as `ReadOnlyMemory<byte>`.
4. **Replay Header**: `X-Idempotency-Replayed: true` is attached to inform the caller that the result was served from cache.

---

## 2. In-Memory Stream Interception in ASP.NET Core

In `IdempotencyMiddleware` and `IdempotentEndpointFilter`, response capturing is performed safely by substituting the response stream:

```csharp
var originalBodyStream = context.Response.Body;
using var capturedStream = new MemoryStream();
context.Response.Body = capturedStream;

try
{
    await _next(context).ConfigureAwait(false);

    capturedStream.Position = 0;
    var responseBytes = capturedStream.ToArray();
    capturedStream.Position = 0;
    await capturedStream.CopyToAsync(originalBodyStream, context.RequestAborted).ConfigureAwait(false);

    var headersToPersist = context.Response.Headers
        .Where(h => !h.Key.StartsWith(':') && !h.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
        .ToDictionary(h => h.Key, h => h.Value.Select(v => v ?? string.Empty).ToArray());

    await store.MarkCompletedAsync(
        tenantId,
        scope,
        key,
        claim.OwnerToken!.Value,
        claim.ConcurrencyVersion!.Value,
        context.Response.StatusCode,
        headersToPersist,
        responseBytes,
        retentionDuration,
        CancellationToken.None).ConfigureAwait(false);
}
finally
{
    context.Response.Body = originalBodyStream;
}
```

---

## 3. Storage Safety and Size Limits

- Hop-by-hop headers (such as `Transfer-Encoding`, `Connection`) and HTTP/2 pseudo-headers are stripped before persistence.
- Large payload streaming operations can bypass body caching via policy configuration to conserve database storage.
