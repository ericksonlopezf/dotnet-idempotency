# ADR-016: No Rate Limiting or Throttling Integration in Core

**Status**: Rejected (Permanent)  
**Date**: 2026-08-27  
**Author**: Erickson López  
**Deciders**: Architecture Team  
**Tags**: rate-limiting, throttling, circuit-breaker, separation-of-concerns, single-responsibility

---

## Context

Community contributors have occasionally suggested adding rate limiting or throttling functionality
to `EricksonLopez.Idempotency` middleware or filters:

1. **Rate limiting by idempotency key**: Prevent a single key from being used more than N times per minute.
2. **Throttling based on tenant**: Apply per-tenant request throttling within the idempotency filter.
3. **Circuit breaker for store failures**: Automatically degrade gracefully when the store is unavailable.

The motivation is that the idempotency middleware is a natural interception point for these controls,
and it reduces the number of middleware components a consumer needs to configure.

---

## Decision

**REJECTED. Rate limiting, throttling, and circuit breaker functionality will never be added to `EricksonLopez.Idempotency`.**

---

## Reasoning

### 1. Separation of Concerns — Each library solves one problem

The name says it all: `EricksonLopez.Idempotency` solves **exactly-once execution guarantees**.
Rate limiting solves **request frequency control**. These are orthogonal concerns with different:
- Configuration models (limits, windows, policies)
- Failure behaviors (429 vs. 409 vs. 503)
- Observability requirements (rate limit headers, retry-after, quota remaining)
- Storage requirements (token bucket counters vs. idempotency records)

Mixing them creates a single component that is harder to understand, test, and maintain.

### 2. The .NET ecosystem already has a first-class, production-grade solution

`Microsoft.AspNetCore.RateLimiting` (built into ASP.NET Core .NET 7+) provides:
- Fixed window, sliding window, token bucket, and concurrency limiters
- Per-endpoint, per-user, and per-tenant limiting via `OnRejected` callbacks
- Standard `429 Too Many Requests` responses with `Retry-After` headers
- Native OpenTelemetry instrumentation

Adding a duplicate (and necessarily inferior) implementation inside `EricksonLopez.Idempotency`
would compete with the platform without adding value. The correct approach is composition:

```csharp
app.UseRateLimiter();  // Microsoft.AspNetCore.RateLimiting
app.UseIdempotency(); // EricksonLopez.Idempotency
```

### 3. Circuit breaker belongs in EricksonLopez.Resilience

The `EricksonLopez.*` ecosystem already includes `EricksonLopez.Resilience`, which handles:
- Circuit breaker patterns
- Retry policies
- Timeout policies
- Hedging

Adding circuit breaker logic inside `EricksonLopez.Idempotency` would duplicate this concern
and create a situation where the two packages have conflicting resilience configurations.

### 4. Adding these concerns increases complexity without proportional value

The consumers who most need rate limiting have diverse requirements:
- Some need per-IP limiting
- Some need per-API-key limiting
- Some need per-tenant limiting
- Some need different limits per endpoint

Implementing a general-purpose rate limiter inside an idempotency library would either be too
opinionated (not fitting most use cases) or too complex (becoming a framework within a framework).

---

## Consequences

### Recommended compositions

```csharp
// Rate limiting (Microsoft.AspNetCore.RateLimiting)
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("default", o => { o.Window = TimeSpan.FromMinutes(1); o.PermitLimit = 100; });
});

// Idempotency (EricksonLopez.Idempotency)
builder.Services.AddAspNetCoreIdempotency(options => { ... });

// Resilience (EricksonLopez.Resilience / Polly)
builder.Services.AddResiliencePipeline("store-pipeline", builder => { builder.AddCircuitBreaker(...); });

// Pipeline order in middleware:
app.UseRateLimiter();     // 1. Rate limiting first — reject before processing
app.UseIdempotency();    // 2. Idempotency second — check/store execution state
```

### For contributors opening PRs with rate limiting features

Pull requests that add rate limiting, throttling, or circuit breaker functionality to
`EricksonLopez.Idempotency.*` projects will be **rejected with reference to this ADR**.

---

## Alternatives Considered

| Alternative | Verdict |
|---|---|
| Rate limiting inside idempotency filter | REJECTED — violates SRP, duplicates ASP.NET Core built-in |
| Per-tenant throttling in idempotency middleware | REJECTED — belongs in rate limiter with tenant-aware policies |
| Circuit breaker for store access | REJECTED — belongs in EricksonLopez.Resilience / Polly |
| Composition via middleware pipeline | ACCEPTED — standard ASP.NET Core pattern |

---

## References

- ADR-001: Why EricksonLopez.Idempotency Exists (scope definition)
- ADR-002: Idempotency is Independent of Resilience
- [Microsoft.AspNetCore.RateLimiting documentation](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit)
- [EricksonLopez.Resilience — circuit breaker integration]
