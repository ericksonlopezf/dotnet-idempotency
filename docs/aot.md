# Native AOT & Trimming Compatibility

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Native AOT & Trimming Mandate

`EricksonLopez.Idempotency` is designed from the ground up to be **100% Native AOT Compatible** on .NET 10.

In `Directory.Build.props`:

```xml
<PropertyGroup>
  <IsAotCompatible>true</IsAotCompatible>
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
</PropertyGroup>
```

---

## 2. Zero Reflection & Source Generated JSON Contexts

Dynamic reflection during runtime serialization causes Native AOT trimming crashes.

To guarantee trimming safety, `SystemTextJsonIdempotencySerializer` and `AspNetCore` adapters use `System.Text.Json.Serialization.JsonSerializerContext`:

```csharp
[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Dictionary<string, string[]>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string[]>))]
[JsonSerializable(typeof(IdempotencyProblemDetails))]
public sealed partial class IdempotencyJsonContext : JsonSerializerContext
{
}
```

> [!IMPORTANT]
> **Consumer Type Registration**: The default `SystemTextJsonIdempotencySerializer` constructor combines
> `IdempotencyJsonContext.Default` with `DefaultJsonTypeInfoResolver` as a fallback for consumer application types.
> `DefaultJsonTypeInfoResolver` uses reflection and is **not AOT-safe**. In AOT-published applications,
> consumer types that are serialized through `IdempotencyEngine.ExecuteAsync<TResult>` (e.g., your domain
> `TResult` types) **must be registered** in a custom `JsonSerializerContext` and injected via
> `IIdempotencySerializer`. The library's own internal types are fully AOT-safe without any additional steps.

---

## 3. AOT Provider Compatibility Matrix

Not all storage providers are equally AOT-compatible. The following table summarizes the compatibility
status:

| Package | Native AOT Compatible | Notes |
|---|---|---|
| `EricksonLopez.Idempotency` (core) | ✅ Yes | Zero reflection, STJ source generators |
| `EricksonLopez.Idempotency.AspNetCore` | ✅ Yes | Zero reflection, source-generated JSON |
| `EricksonLopez.Idempotency.Mediator` | ✅ Yes | Zero reflection |
| `EricksonLopez.Idempotency.PostgreSql` | ✅ Yes | Npgsql 10.x is AOT-compatible |
| `EricksonLopez.Idempotency.SqlServer` | ✅ Yes | Microsoft.Data.SqlClient 5.x is AOT-compatible |
| `EricksonLopez.Idempotency.Sqlite` | ✅ Yes | Microsoft.Data.Sqlite 10.x is AOT-compatible |
| `EricksonLopez.Idempotency.MySql` | ✅ Yes | MySqlConnector 2.x is AOT-compatible |
| `EricksonLopez.Idempotency.MariaDb` | ✅ Yes | MySqlConnector 2.x is AOT-compatible |
| `EricksonLopez.Idempotency.Redis` | ✅ Yes | StackExchange.Redis 2.8+ is AOT-compatible |
| `EricksonLopez.Idempotency.Oracle` | ⚠️ **NO** | `Oracle.ManagedDataAccess.Core` uses reflection internally; AOT publishing will produce trimming warnings and may fail at runtime |
| `EricksonLopez.Idempotency.Testing` | ✅ Yes | In-memory only, no native dependencies |

> [!WARNING]
> **Oracle AOT Limitation**: `EricksonLopez.Idempotency.Oracle` is **not Native AOT compatible** because
> `Oracle.ManagedDataAccess.Core` uses reflection internally. Do NOT use this package in applications
> published with `PublishAot=true`. For Oracle databases in AOT environments, track
> [Oracle issue tracker](https://github.com/oracle/dotnet-db-samples) for future AOT support.

---

## 4. AOT Compilation Verification

To publish and test Native AOT compilation:

```bash
dotnet publish src/EricksonLopez.Idempotency/EricksonLopez.Idempotency.csproj \
    -c Release \
    -r win-x64 \
    --self-contained \
    /p:PublishAot=true
```
Expected output: **0 Trimming Warnings, 0 AOT Compatibility Warnings.**

> [!NOTE]
> Do not include the Oracle provider project in AOT publish commands.
> The AOT Smoke Test (`tests/EricksonLopez.Idempotency.AotSmokeTest/`) specifically excludes the Oracle provider to maintain a green AOT baseline.
