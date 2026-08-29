# Multi-Tenancy Isolation Architecture

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Multi-Tenant Partitioning

Multi-tenancy is a first-class citizen across all components in `EricksonLopez.Idempotency`.

Every storage operation requires a mandatory `tenantId: Guid`:

```csharp
Task<IdempotencyClaimResult> TryAcquireAsync(
    Guid tenantId,
    string scope,
    IdempotencyKey key,
    string fingerprint,
    TimeSpan leaseDuration,
    TimeSpan retentionDuration,
    CancellationToken cancellationToken = default);
```

In single-tenant deployments, `Guid.Empty` is used as the default tenant identifier.

---

## 2. Extraction from HTTP Context & Configurable Resolvers

In `EricksonLopez.Idempotency.AspNetCore`, the tenant is extracted dynamically:

1. **Configurable Extractor**: Via `UseTenantIdExtractor` in `IdempotencyOptions`:
   ```csharp
   builder.Services.AddAspNetCoreIdempotency(options =>
   {
       options.UseTenantIdExtractor(httpContext =>
       {
           // Custom resolution: header, route parameter, or custom tenancy context
           if (httpContext.Request.Headers.TryGetValue("X-Tenant-ID", out var headerVal) &&
               Guid.TryParse(headerVal, out var tenantId))
           {
               return tenantId;
           }
           return Guid.Empty;
       });
   });
   ```

2. **Default Fallback Resolution**:
   If no custom extractor is configured, the adapter automatically checks:
   - `httpContext.Items["TenantId"]` (set by multi-tenancy middleware).
   - `httpContext.User.FindFirst("tenant_id")` JWT claim.
   - Defaults to `Guid.Empty` if not present.

---

## 3. Database Isolation Guarantees

In all supported storage providers (PostgreSQL, SQL Server, MySQL, MariaDB, Oracle, SQLite), `tenant_id` forms the leading column in the composite primary key index:

```sql
CONSTRAINT pk_idempotency_records PRIMARY KEY (tenant_id, scope, idempotency_key)
```

This guarantees:
- **Zero cross-tenant data leakage**: Records from Tenant A cannot be claimed, viewed, or overwritten by Tenant B even if they supply the same `IdempotencyKey`.
- **Collocated B-Tree Indexing**: Queries are scoped immediately to the tenant partition.
- **PostgreSQL Row-Level Security (RLS) Compatibility**: Integrates cleanly with `tenant_id = current_setting('app.current_tenant_id')::uuid`.
