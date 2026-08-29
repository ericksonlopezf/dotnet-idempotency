# ADR-014: Modern Target Frameworks Policy (net8.0;net9.0;net10.0) and Legacy Down-Level Rejection

**Status**: Accepted (Permanent Policy)  
**Date**: 2026-08-27  
**Author**: Erickson López  
**Deciders**: Architecture Team  
**Tags**: target-framework, net8, net9, net10, native-aot, backwards-compatibility

---

## Context

Community members and enterprise evaluators require clarity regarding target framework support for `EricksonLopez.Idempotency`:

1. `.NET Standard 2.0` / `.NET Framework 4.8` — requested for legacy interoperability.
2. `.NET 6` / `.NET 7` — legacy versions (End of Life).
3. `.NET 8` — LTS (active enterprise support).
4. `.NET 9` — STS (active support).
5. `.NET 10` — latest LTS / primary target.

The motivations and tradeoffs are:
- Enterprise teams are operating across modern .NET versions (.NET 8 LTS, .NET 9 STS, .NET 10 LTS).
- Other legacy libraries (e.g., `IdempotentAPI`) target `.NET Standard 2.0` with reflection-heavy implementations.
- `EricksonLopez.Idempotency` prioritizes **100% Native AOT compatibility, zero reflection, and high-performance Spans**.

---

## Decision

1. **Multi-Targeting Modern Supported .NET**: `EricksonLopez.Idempotency` multi-targets **`net8.0;net9.0;net10.0`** centrally across all published NuGet packages (`Directory.Build.props`).
2. **Permanent Rejection of Legacy Down-Level Frameworks**: Multi-targeting for `.NET Standard 2.0`, `.NET Framework`, `.NET Core 3.1`, `.NET 6`, and `.NET 7` is **strictly rejected**.

---

## Reasoning

### 1. Modern .NET Runtimes Provide Native AOT and Trimming Support

The primary architectural differentiator of `EricksonLopez.Idempotency` is **Native AOT and Trimming compatibility**:

| .NET Version | Support Status in `EricksonLopez.Idempotency` | Native AOT Status |
|---|---|---|
| .NET Standard 2.0 | ❌ REJECTED | NOT SUPPORTED |
| .NET Framework 4.8 | ❌ REJECTED | NOT SUPPORTED |
| .NET 6 / .NET 7 | ❌ REJECTED (EOL) | Preview / Limited |
| .NET 8 (LTS) | ✅ SUPPORTED (`net8.0`) | Supported |
| .NET 9 (STS) | ✅ SUPPORTED (`net9.0`) | Mature |
| .NET 10 (LTS) | ✅ PRIMARY TARGET (`net10.0`) | First-class, Full Ecosystem |

### 2. High-Performance Cryptography & Source Generators

`EricksonLopez.Idempotency` relies on high-performance Spans, incremental SHA-256 hashing, and compile-time Source Generators:

- `System.Text.Json` compile-time Source Generators (`IdempotencyJsonContext`) are first-class on .NET 8, 9, and 10.
- `IncrementalHash.CreateHash(HashAlgorithmName.SHA256)` with `ReadOnlySpan<byte>` and `stackalloc` buffers.
- `[LoggerMessage]` high-performance logging source generators.
- `ArgumentNullException.ThrowIfNull()` and modern C# language features.

Backporting to `.NET Standard 2.0` would force fallback to reflection-based serialization, heap allocations, and legacy crypto wrappers, violating the core architectural guarantees of the framework.

### 3. Clear Market Segmentation

- **Legacy / .NET Standard 2.0 projects**: Recommend alternative libraries (such as `IdempotentAPI`) until migration to modern .NET is completed.
- **Enterprise Modern .NET (.NET 8, 9, 10)**: `EricksonLopez.Idempotency` provides Native AOT, multi-tenancy, distributed fencing tokens, and zero-allocation pipelines.

---

## Consequences

- **Positive**: All active enterprise .NET consumers (.NET 8 LTS, .NET 9 STS, .NET 10 LTS) can adopt `EricksonLopez.Idempotency` directly.
- **Positive**: Zero compromise on Native AOT, trimming safety, and zero-allocation performance.
- **Negative**: Legacy .NET Framework / .NET Standard 2.0 applications cannot consume the packages without upgrading to modern .NET.

---

## Alternatives Considered

| Alternative | Verdict |
|---|---|
| `.NET Standard 2.0` targeting | REJECTED — Incompatible with Native AOT, Spans, and STJ source generators |
| `.NET 6` / `.NET 7` targeting | REJECTED — Frameworks are End of Life |
| `.NET 10` only (single TFM) | SUPERSEDED — Multi-targeting `net8.0;net9.0;net10.0` allows broad LTS enterprise adoption without compromising AOT |
| `net8.0;net9.0;net10.0` multi-targeting | ACCEPTED — Aligns with enterprise adoption and LTS/STS release schedules |

---

## References

- ADR-010: Native AOT Source Generators Strategy
- ADR-012: No Newtonsoft.Json Support
- `Directory.Build.props` — `<TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>`
- [.NET Support Policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
