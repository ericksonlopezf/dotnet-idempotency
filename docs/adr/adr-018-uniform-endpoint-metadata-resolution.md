# ADR-018: Uniform Endpoint Metadata Resolution Across Middleware and Minimal API Filters

## Status
Accepted

## Date
2026-09-15

**Status**: Accepted  
**Date**: 2026-09-15  
**Author**: Erickson López  
**Deciders**: Architecture Team  
**Tags**: aspnetcore, minimal-api, middleware, endpoint-filter, metadata, IdempotentAttribute

---

## Context

In ASP.NET Core applications using `EricksonLopez.Idempotency.AspNetCore`, developers can enforce idempotency via two complementary mechanisms:
1. **Global/Route-Level Middleware** (`IdempotencyMiddleware` via `UseIdempotency()`).
2. **Minimal API Endpoint Filters** (`IdempotentEndpointFilter` via `.WithIdempotency()`).

Prior to this decision, `IdempotencyMiddleware` inspected the matching endpoint for `IdempotentAttribute` metadata (e.g., custom `Scope`, `LeaseDurationSeconds`, `RetentionDurationDays`, `Required`, and `Enabled`), whereas `IdempotentEndpointFilter` only relied on global `IdempotencyOptions` and hardcoded the request path as the scope.

### The Problem

Minimal API routes decorated with:
```csharp
app.MapPost("/api/v1/orders", handler)
   .WithIdempotency()
   .WithMetadata(new IdempotentAttribute
   {
       Scope = "orders.create",
       LeaseDurationSeconds = 45,
       RetentionDurationDays = 14,
       Required = true
   });
```
compiled successfully, but in execution the endpoint filter ignored the attached `IdempotentAttribute`. Consequently:
- The scope defaulted to `/api/v1/orders` rather than `orders.create`.
- Lease and retention durations defaulted to global settings rather than the endpoint-configured values.
- Opt-out via `[Idempotent(Enabled = false)]` was not recognized by the filter.

This discrepancy led to behavioral asymmetry between controllers and Minimal APIs and violated developer expectations.

---

## Decision

We establish a **Uniform Endpoint Metadata Resolution Policy** across both `IdempotencyMiddleware` and `IdempotentEndpointFilter`:

1. **Explicit Precedence**:
   - Both components inspect `context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<IdempotentAttribute>()`.
   - If present:
     - `Scope`: `idempotentAttr.Scope ?? requestPath`.
     - `LeaseDuration`: `idempotentAttr != null ? TimeSpan.FromSeconds(idempotentAttr.LeaseDurationSeconds) : _options.DefaultLeaseDuration`.
     - `RetentionDuration`: `idempotentAttr != null ? TimeSpan.FromDays(idempotentAttr.RetentionDurationDays) : _options.DefaultRetentionDuration`.
     - `Required`: `idempotentAttr.Required ?? _options.RequireIdempotencyKey`.
     - `Enabled`: If `idempotentAttr.Enabled == false`, bypass idempotency processing immediately and delegate to `next`.

2. **Persistence Guarantee**:
   - The resolved `retentionDuration` is passed to both `_store.TryAcquireAsync` and `_store.MarkCompletedAsync`.

---

## Consequences

### Positive
- **100% Behavioral Parity**: Controllers (using Middleware + attributes) and Minimal APIs (using Endpoint Filters + attributes/metadata) behave identically.
- **Zero API Breaking Changes**: No public signatures were modified; existing code gains metadata awareness without migration overhead.
- **Granular Control**: Developers can configure distinct lease and retention durations per endpoint in Minimal APIs without creating multiple global options instances.

### Negative / Trade-offs
- Endpoint metadata lookup (`GetEndpoint()?.Metadata.GetMetadata<T>()`) adds a small reference lookup per request, but this is negligible and optimized in ASP.NET Core's metadata collection.
