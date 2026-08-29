# ADR-012: No Newtonsoft.Json Support

**Status**: Rejected (Permanent)  
**Date**: 2026-08-27  
**Author**: Erickson López  
**Deciders**: Architecture Team  
**Tags**: serialization, native-aot, dependencies, backwards-compatibility

---

## Context

`Newtonsoft.Json` (Json.NET) is the most widely used JSON serialization library in the .NET ecosystem,
with hundreds of millions of NuGet downloads. Several contributors and evaluators have requested support
for `Newtonsoft.Json` in `EricksonLopez.Idempotency`, either as a primary serializer or as an alternative
to the current `System.Text.Json` (STJ) implementation.

The primary motivation behind these requests is:
1. Existing codebases that already depend on `Newtonsoft.Json` and want consistent serialization across the stack.
2. Specific serialization behaviors that `Newtonsoft.Json` supports by default but STJ does not (e.g., `[JsonConverter]` from `Newtonsoft`).
3. Backward compatibility with existing idempotency stores that serialized responses using `Newtonsoft.Json`.

---

## Decision

**REJECTED. `Newtonsoft.Json` will never be added as a dependency to any package in the `EricksonLopez.Idempotency` ecosystem.**

---

## Reasoning

### 1. Native AOT is a core differentiator — not a feature flag

The most fundamental reason for rejecting `Newtonsoft.Json` is that it is **fundamentally incompatible with Native AOT and IL Trimming**.

`Newtonsoft.Json` relies on **runtime reflection** for type discovery, contract resolution, and serialization:
```csharp
// Newtonsoft.Json — uses reflection at runtime
JsonConvert.SerializeObject(myObject); // discovers members via reflection
```

When published in AOT mode (`PublishAot=true`), the .NET trimmer removes any code path not statically
reachable. `Newtonsoft.Json`'s reflection-based serialization cannot be preserved by the trimmer, resulting
in `MissingMethodException`, `TypeLoadException`, or `NullReferenceException` at runtime.

The `Directory.Build.props` in this repository enforces:
```xml
<IsAotCompatible>true</IsAotCompatible>
<EnableTrimAnalyzer>true</EnableTrimAnalyzer>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

Adding `Newtonsoft.Json` as a direct dependency would generate immediate build errors due to trimming
analyzer violations — which are treated as build errors. This means **adding `Newtonsoft.Json` would
break the build** of every project in the solution.

### 2. STJ source generators provide equivalent capabilities in AOT-safe code

`System.Text.Json` (STJ) with `JsonSourceGenerationOptions` and `JsonSerializerContext` provides all the
serialization capabilities required by this library — with zero trimming warnings and full AOT compatibility:

```csharp
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(MyRequest))]
public sealed partial class MyJsonContext : JsonSerializerContext { }
```

The current `SystemTextJsonIdempotencySerializer` and `IdempotencyJsonContext` provide complete,
AOT-safe serialization for all response caching and request fingerprinting operations.

### 3. The `IIdempotencySerializer` SPI already enables Newtonsoft-based serialization externally

Consumers who need `Newtonsoft.Json` behavior can implement the `IIdempotencySerializer` interface:

```csharp
public sealed class NewtonsoftIdempotencySerializer : IIdempotencySerializer
{
    public ReadOnlyMemory<byte> Serialize<T>(T value) =>
        Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value));

    public T? Deserialize<T>(ReadOnlyMemory<byte> bytes) =>
        JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(bytes.Span));
}

// Registration:
services.AddSingleton<IIdempotencySerializer, NewtonsoftIdempotencySerializer>();
```

This keeps the `Newtonsoft.Json` dependency entirely in the consumer's project and does not contaminate the
library itself or its AOT guarantees.

### 4. Accepting Newtonsoft would establish a precedent that destroys the AOT differentiator

Once `Newtonsoft.Json` is accepted as a dependency for one scenario, contributors will expect parity:
- Fingerprint generation using Newtonsoft
- Response storage using Newtonsoft
- ProblemDetails serialization using Newtonsoft

The result would be a library with a Newtonsoft code path that **cannot be tested in AOT mode**, creating a
two-tier quality problem and gradually eroding the AOT differentiator.

---

## Consequences

### What happens if a consumer needs Newtonsoft.Json

1. Implement `IIdempotencySerializer` with `Newtonsoft.Json` in their own project.
2. Register it as a singleton: `services.AddSingleton<IIdempotencySerializer, YourCustomSerializer>()`.
3. Do NOT use `PublishAot=true` if using `Newtonsoft.Json` anywhere in the application.

### What happens to contributors who open PRs with Newtonsoft.Json

Pull requests that add `Newtonsoft.Json` as a project or package dependency to any `EricksonLopez.Idempotency.*`
project will be **rejected with reference to this ADR**.

---

## Alternatives Considered

| Alternative | Verdict |
|---|---|
| `Newtonsoft.Json` as primary serializer | REJECTED — AOT incompatible |
| `Newtonsoft.Json` as optional secondary serializer via conditional compilation | REJECTED — creates dual code paths, AOT guarantees are per-assembly |
| Separate `EricksonLopez.Idempotency.Newtonsoft` package | REJECTED — the package would inherit the Newtonsoft AOT incompatibility and confuse consumers about which combination is AOT-safe |
| `IIdempotencySerializer` custom implementation (consumer-owned) | ACCEPTED — this is the supported path |

---

## References

- ADR-010: Native AOT Source Generators Strategy
- [System.Text.Json Source Generation — Microsoft Docs](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)
- [Newtonsoft.Json and AOT](https://github.com/JamesNK/Newtonsoft.Json/issues/2458) — acknowledged as unsupported by James Newton-King
- `Directory.Build.props` — `IsAotCompatible=true`, `TreatWarningsAsErrors=true`
