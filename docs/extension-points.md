# Extension Points & SPI Reference

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## Overview

`EricksonLopez.Idempotency` is built around a clean SPI (Service Provider Interface) model.
In addition to the primary storage SPI (`IIdempotencyStore`, `ITransactionalIdempotencyStore`) and the
fingerprinting SPI (`IIdempotencyFingerprintGenerator`), the following interfaces allow fine-grained
customization of the idempotency pipeline.

---

## 1. `IIdempotencyKeyProvider<TContext>`

**Namespace**: `EricksonLopez.Idempotency`  
**Assembly**: `EricksonLopez.Idempotency.Abstractions`

```csharp
public interface IIdempotencyKeyProvider<in TContext>
{
    /// <summary>
    /// Attempts to resolve an idempotency key from the specified context.
    /// Returns null if no key can be resolved.
    /// </summary>
    ValueTask<IdempotencyKey?> TryGetKeyAsync(TContext context, CancellationToken cancellationToken = default);
}
```

### Purpose

`IIdempotencyKeyProvider<TContext>` is the SPI for resolving an idempotency key from an arbitrary request
or message context. It is useful when the idempotency key is not carried in the standard HTTP header but
is instead embedded in the request body, a message envelope, or derived from request properties.

### Example: Key from Request Body

```csharp
public sealed class OrderRequestKeyProvider : IIdempotencyKeyProvider<CreateOrderRequest>
{
    public ValueTask<IdempotencyKey?> TryGetKeyAsync(
        CreateOrderRequest context,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(context.ClientRequestId))
            return ValueTask.FromResult<IdempotencyKey?>(new IdempotencyKey(context.ClientRequestId));

        return ValueTask.FromResult<IdempotencyKey?>(null);
    }
}
```

### Registration

```csharp
services.AddSingleton<IIdempotencyKeyProvider<CreateOrderRequest>, OrderRequestKeyProvider>();
```

---

## 2. `IIdempotencySerializer`

**Namespace**: `EricksonLopez.Idempotency`  
**Assembly**: `EricksonLopez.Idempotency.Abstractions`

```csharp
public interface IIdempotencySerializer
{
    byte[] Serialize<T>(T value);
    T? Deserialize<T>(ReadOnlyMemory<byte> bytes);
}
```

### Purpose

`IIdempotencySerializer` is the SPI for serializing and deserializing response payloads stored in the
idempotency record. The default implementation is `SystemTextJsonIdempotencySerializer` which uses
`System.Text.Json` with source-generated contexts.

### When to replace the default

- **Custom types not registered in a JsonSerializerContext**: If your response types are not known at
  compile time, implement a custom serializer that registers them explicitly.
- **Alternative formats**: For binary protocols (e.g., MessagePack, Protobuf) or performance-critical
  scenarios that require zero-allocation serialization.
- **Native AOT**: For full AOT safety with your consumer types (see [docs/aot.md](aot.md)).

### Example: MessagePack Serializer

```csharp
using MessagePack;

public sealed class MessagePackIdempotencySerializer : IIdempotencySerializer
{
    public byte[] Serialize<T>(T value)
        => MessagePackSerializer.Serialize(value);

    public T? Deserialize<T>(ReadOnlyMemory<byte> bytes)
        => MessagePackSerializer.Deserialize<T>(bytes);
}
```

### Registration

```csharp
// Replaces the default SystemTextJsonIdempotencySerializer
services.AddSingleton<IIdempotencySerializer, MessagePackIdempotencySerializer>();
```

> [!WARNING]
> The serializer must be registered **before** `AddIdempotencyCore()` or the DI container will use
> the last-registered implementation. Use `services.Replace(...)` if registering after `AddIdempotencyCore()`.

---

## 3. `IIdempotencyContextAccessor` & `IdempotencyContext`

**Namespace**: `EricksonLopez.Idempotency`  
**Assembly**: `EricksonLopez.Idempotency.Abstractions`

```csharp
public interface IIdempotencyContextAccessor
{
    IdempotencyContext? IdempotencyContext { get; set; }
}

public sealed class IdempotencyContext
{
    public IdempotencyKey Key { get; init; }
    public string Scope { get; init; }
    public Guid TenantId { get; init; }
    public string? AuthenticatedSubject { get; init; }
    public bool IsReplay { get; init; }
}
```

### Purpose

`IIdempotencyContextAccessor` provides ambient access to the current `IdempotencyContext` within the
scope of an idempotent execution. It is analogous to `IHttpContextAccessor` in ASP.NET Core.

The registered implementation (`AsyncLocalIdempotencyContextAccessor`) uses `AsyncLocal<T>` to flow
the context across async continuations without requiring explicit parameter passing.

### When to use

- **Domain event enrichment**: Attach the idempotency key and tenant ID to domain events without
  threading these values through application layer parameters.
- **Logging & Diagnostics**: Enrich log entries with the current idempotency key and replay status.
- **Audit trails**: Record which operations were replays vs. original executions.

### Example: Reading the current idempotency context

```csharp
public sealed class PaymentCommandHandler
{
    private readonly IIdempotencyContextAccessor _contextAccessor;
    private readonly ILogger<PaymentCommandHandler> _logger;

    public PaymentCommandHandler(
        IIdempotencyContextAccessor contextAccessor,
        ILogger<PaymentCommandHandler> logger)
    {
        _contextAccessor = contextAccessor;
        _logger = logger;
    }

    public async Task<PaymentResponse> Handle(CreatePaymentCommand command, CancellationToken ct)
    {
        var idempotencyCtx = _contextAccessor.IdempotencyContext;

        _logger.LogInformation(
            "Processing payment. IdempotencyKey={Key}, IsReplay={IsReplay}, TenantId={TenantId}",
            idempotencyCtx?.Key,
            idempotencyCtx?.IsReplay,
            idempotencyCtx?.TenantId);

        // ... domain logic ...
    }
}
```

### Registration

`IIdempotencyContextAccessor` is automatically registered by `AddIdempotencyCore()`.
No additional registration is required.

```csharp
// IIdempotencyContextAccessor is registered automatically:
builder.Services.AddIdempotencyCore(options => { ... });

// Inject directly:
public class MyService(IIdempotencyContextAccessor accessor) { }
```

---

## Summary Table

| Interface | Purpose | Default Implementation | Replaceable |
|---|---|---|---|
| `IIdempotencyStore` | Persistence SPI | (no default; choose a provider) | ✅ Yes |
| `ITransactionalIdempotencyStore` | Transactional persistence SPI | (no default) | ✅ Yes |
| `IIdempotencyFingerprintGenerator` | Fingerprint computation | `IdempotencyFingerprintHasher` (SHA-256) | ✅ Yes |
| `IIdempotencySerializer` | Payload serialization | `SystemTextJsonIdempotencySerializer` | ✅ Yes |
| `IIdempotencyKeyProvider<TContext>` | Key resolution from context | (no default; opt-in) | ✅ Yes |
| `IIdempotencyContextAccessor` | Ambient execution context | `AsyncLocalIdempotencyContextAccessor` | ✅ Yes |
| `IIdempotencyPolicy` | Caching policy | `DefaultIdempotencyPolicy` | ✅ Yes |
