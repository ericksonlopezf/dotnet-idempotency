# Integration with EricksonLopez.Result

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Functional Monadic Outcomes vs Exceptions

In Domain-Driven Design and Clean Architecture, business failures (such as validation errors, credit limit exceeded, or concurrent modification conflicts) are **domain concepts**, not exceptional runtime crashes.

`EricksonLopez.Idempotency.Result` integrates directly with `EricksonLopez.Result` to provide functional error mapping without throwing expensive control-flow exceptions.

---

## 2. Standardized Idempotency Errors

`IdempotencyErrors` provides centralized factory methods returning structured domain errors:

```csharp
public static class IdempotencyErrors
{
    public static Error InFlightConflict(string key) =>
        Error.Conflict(
            code: "Idempotency.InFlightConflict",
            description: $"An identical operation with idempotency key '{key}' is currently being processed.");

    public static Error FingerprintMismatch(string key) =>
        Error.Validation(
            code: "Idempotency.FingerprintMismatch",
            description: $"The idempotency key '{key}' was previously used with a different request payload.");

    public static Error LeaseLost(string key) =>
        Error.Failure(
            code: "Idempotency.LeaseLost",
            description: $"Ownership lease for idempotency key '{key}' was lost before completion.");
}
```

---

## 3. Monadic Execution Pattern

```csharp
public async Task<Result<OrderDto>> ProcessOrderAsync(
    Guid tenantId,
    IdempotencyKey key,
    CreateOrderCommand command,
    CancellationToken cancellationToken)
{
    var fingerprint = IdempotencyFingerprintHasher.Compute(
        "CreateOrder", "orders", tenantId.ToString(), null, command.PayloadBytes);

    var claim = await _store.TryAcquireAsync(
        tenantId, "orders", key, fingerprint, TimeSpan.FromSeconds(30), TimeSpan.FromDays(7), cancellationToken);

    // Map claim failures directly to Result<T> failure monads:
    var errorResult = claim.AsErrorResult<OrderDto>(key.Value);
    if (errorResult is not null)
    {
        return errorResult;
    }

    if (claim.IsReplay && claim.CachedResponse is not null)
    {
        var cachedOrder = _serializer.Deserialize<OrderDto>(claim.CachedResponse.Body);
        return Result<OrderDto>.Success(cachedOrder!);
    }

    // Execute domain operation
    var result = await _orderService.CreateAsync(command, cancellationToken);

    if (result.IsSuccess)
    {
        var serialized = _serializer.Serialize(result.Value);
        await _store.MarkCompletedAsync(
            tenantId, "orders", key, claim.OwnerToken!.Value, claim.ConcurrencyVersion!.Value,
            200, new Dictionary<string, string[]>(), serialized, TimeSpan.FromDays(7), CancellationToken.None);
    }
    else
    {
        await _store.MarkFailedAsync(
            tenantId, "orders", key, claim.OwnerToken!.Value, claim.ConcurrencyVersion!.Value, CancellationToken.None);
    }

    return result;
}
```
