# ASP.NET Core Integration Guide

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Minimal APIs: Endpoint Filter Approach

Modern ASP.NET Core Minimal APIs leverage endpoint filters for declarative, high-performance interception.

```csharp
app.MapPost("/api/v1/subscriptions", async (SubscriptionRequest request, SubscriptionService service) =>
{
    var result = await service.CreateSubscriptionAsync(request);
    return Results.Created($"/subscriptions/{result.Id}", result);
})
.WithIdempotency()
.WithMetadata(new IdempotentAttribute
{
    Scope = "subscriptions",
    Required = true,
    LeaseDurationSeconds = 45,
    RetentionDurationDays = 30
});
```

---

## 2. Controller-Based Applications: Middleware & Attributes

For standard MVC / API Controllers, decorate actions with `[Idempotent]`:

```csharp
[ApiController]
[Route("api/v1/[controller]")]
public class InvoicesController : ControllerBase
{
    [HttpPost]
    [Idempotent(Scope = "invoices", LeaseDurationSeconds = 60, RetentionDurationDays = 14, Required = true)]
    public async Task<IActionResult> CreateInvoice([FromBody] InvoiceRequest request)
    {
        var invoice = await _invoiceService.GenerateAsync(request);
        return Ok(invoice);
    }
}
```

Add the middleware in `Program.cs`:

```csharp
app.UseRouting();
app.UseIdempotency(); // Placed between UseRouting() and UseEndpoints()
app.MapControllers();
```

---

## 3. Standardized HTTP Problem Details Responses

| Scenario | HTTP Status | Problem Details Title | RFC Spec |
|---|---|---|---|
| Missing mandatory `Idempotency-Key` | `400 Bad Request` | Missing Idempotency Key | RFC 9110 §15.5.1 |
| Concurrent in-flight request on same key | `409 Conflict` (with `Retry-After: 2`) | Operation In Flight | RFC 9110 §15.5.10 |
| Reused key with different payload | `409 Conflict` | Idempotency Key Conflict | RFC 9110 §15.5.10 |
| Successful cached response served | `200/201` (with `X-Idempotency-Replayed: true`) | - | - |

---

## 4. Multi-Tenant Idempotency: `UseTenantIdExtractor`

By default, the idempotency adapter extracts the tenant identifier from `HttpContext.Items["TenantId"]` or the
`tenant_id` JWT claim. For custom tenant resolution strategies, use the `UseTenantIdExtractor` extension method:

```csharp
builder.Services.AddAspNetCoreIdempotency(options =>
{
    options.UseTenantIdExtractor(httpContext =>
    {
        // Custom resolution: resolve from header, route parameter, or tenancy middleware
        if (httpContext.Request.Headers.TryGetValue("X-Tenant-ID", out var headerVal) &&
            Guid.TryParse(headerVal, out var tenantId))
        {
            return tenantId;
        }
        return Guid.Empty;
    });
});
```

`UseTenantIdExtractor` sets the `IdempotencyOptions.TenantIdExtractor` delegate and takes priority over
all default resolution strategies. Single-tenant applications can omit this configuration and rely on `Guid.Empty`.

---

## 5. Route Group Idempotency: `WithIdempotency(RouteGroupBuilder)`

To apply idempotency enforcement to an entire group of routes, use the `RouteGroupBuilder` overload of `WithIdempotency()`:

```csharp
var payments = app.MapGroup("/api/v1/payments")
    .WithIdempotency()   // applies IdempotentEndpointFilter to every route in this group
    .RequireAuthorization();

payments.MapPost("/charge", HandleCharge);
payments.MapPost("/refund", HandleRefund);
```

This is functionally equivalent to calling `.WithIdempotency()` on each individual `RouteHandlerBuilder`,
but reduces repetition when all endpoints in a group share the same idempotency policy.

> [!NOTE]
> The group-level `.WithIdempotency()` respects per-endpoint `[Idempotent(Enabled = false)]` attributes,
> which allows individual endpoints to opt out of group-level enforcement.

---

## 6. Security: Response Header Sanitization (`ResponseHeadersBlocklist`)

When caching HTTP responses for replay, caching sensitive headers such as session cookies, bearer tokens, or authentication challenges creates critical security vulnerabilities (credential leakage, session hijacking, or cross-tenant exposure).

`IdempotencyOptions.ResponseHeadersBlocklist` provides a configurable, case-insensitive set of header names that are automatically stripped before response headers are persisted to the idempotency store.

By default, the blocklist contains:
- `Set-Cookie`
- `Set-Cookie2`
- `Authorization`
- `Proxy-Authenticate`
- `Proxy-Authorization`
- `WWW-Authenticate`

You can extend the blocklist with custom application headers:

```csharp
builder.Services.AddAspNetCoreIdempotency(options =>
{
    options.ResponseHeadersBlocklist.Add("X-Custom-Session-Token");
    options.ResponseHeadersBlocklist.Add("X-Api-Key");
});
```
