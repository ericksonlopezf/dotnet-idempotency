# Contributing to EricksonLopez.Idempotency

Thank you for your interest in contributing to **EricksonLopez.Idempotency**! We welcome contributions that improve code quality, extend storage engine support, enhance performance, or refine technical documentation.

---

## 1. Code of Conduct

By participating in this project, you agree to abide by the [Code of Conduct](CODE_OF_CONDUCT.md). Please report unacceptable behavior to <ericksonlopezf@gmail.com>.

---

## 2. Development Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`10.0.x` or later — installs toolsets to build `net8.0`, `net9.0`, and `net10.0` targets)
- Git 2.40+
- Optional: Docker / Testcontainers (for running local integration tests against PostgreSQL, SQL Server, MySQL, MariaDB, Oracle, and Redis)
- IDE: Visual Studio 2022+, Visual Studio Code (with C# Dev Kit), or JetBrains Rider

> **Note**: No `global.json` is present in this repository. The build requires the latest `10.0.x` SDK toolchain to compile all three target frameworks (`net8.0`, `net9.0`, `net10.0`).

---

## 3. Building the Solution

Clone the repository and build the entire solution in `Release` configuration:

```bash
git clone https://github.com/ericksonlopezf/dotnet-idempotency.git
cd dotnet-idempotency
dotnet restore EricksonLopez.Idempotency.slnx
dotnet build EricksonLopez.Idempotency.slnx --configuration Release
```

The build enforces `TreatWarningsAsErrors=true`, code quality rules, and trimming analyzers. A clean build must produce **0 warnings and 0 errors**.

---

## 4. Running Tests

Run the full automated test suite:

```bash
# Run all unit, integration, and architecture tests
dotnet test EricksonLopez.Idempotency.slnx --configuration Release

# Run architecture verification tests specifically
dotnet test tests/EricksonLopez.Idempotency.ArchitectureTests/EricksonLopez.Idempotency.ArchitectureTests.csproj

# Run integration tests (high-concurrency race condition validation)
dotnet test tests/EricksonLopez.Idempotency.IntegrationTests/EricksonLopez.Idempotency.IntegrationTests.csproj

# Run Native AOT smoke tests
dotnet publish tests/EricksonLopez.Idempotency.AotSmokeTest/EricksonLopez.Idempotency.AotSmokeTest.csproj \
    -c Release -r linux-x64 --self-contained -o ./aot-output
./aot-output/EricksonLopez.Idempotency.AotSmokeTest
```

---

## 5. Running Mutation Tests

Mutation testing is performed with [Stryker.NET](https://stryker-mutator.io/). Each package has a dedicated configuration file:

```bash
# Install Stryker globally
dotnet tool install -g dotnet-stryker

# Run mutation tests for the core package
dotnet-stryker --config-file stryker-core-config.json

# Other packages: stryker-abstractions-config.json, stryker-aspnetcore-config.json,
# stryker-postgresql-config.json, stryker-redis-config.json, etc.
```

Mutation score thresholds are enforced by `scripts/record-stryker-result.js` in CI.

---

## 6. Running the Showcase

Execute the interactive 11-level progressive demonstration:

```bash
# Automated run through all 11 levels
dotnet run --project samples/Showcase/EricksonLopez.Idempotency.Showcase.csproj

# Interactive step-by-step mode
dotnet run --project samples/Showcase/EricksonLopez.Idempotency.Showcase.csproj -- --interactive
```

---

## 7. Running Benchmarks

Execute BenchmarkDotNet performance tests:

```bash
dotnet run --project benchmarks/EricksonLopez.Idempotency.Benchmarks/EricksonLopez.Idempotency.Benchmarks.csproj --configuration Release
```

> **Benchmark policy**: PRs that modify `src/**` or `benchmarks/**` are subject to the automated benchmark regression gate (`.github/workflows/benchmark-regression-gate.yml`), which compares performance against the stored baseline in `benchmarks/results/`. Regressions exceeding 10% cause the build to fail.

---

## 8. Repository Compliance

The repository enforces architectural invariants via `scripts/verify-compliance.ps1`, which runs as the first job in CI:

1. All C# source files in `src/` must begin with `// Copyright © Erickson Lopez. MIT License.`
2. Documentation files in `docs/` must use `kebab-case.md` naming.
3. Zero `[Obsolete]` attribute usages in production code.
4. "One Type Per File" invariant in `src/`.
5. Canonical contact email normalization (`ericksonlopezf@gmail.com`).

You can run the compliance check locally:

```powershell
pwsh -File scripts/verify-compliance.ps1
```

---

## 9. Architectural Guidelines & Standards

1. **Native AOT & Trimming Invariant**:
   - Zero reflection on hot paths.
   - Use compile-time Source Generators for `System.Text.Json` (`IdempotencyJsonContext`).
   - Validate trimming safety using `dotnet publish -c Release -r linux-x64 /p:PublishAot=true`.
2. **Clean Architecture Isolation**:
   - `EricksonLopez.Idempotency.Abstractions` must have **zero external dependencies**.
   - Storage adapters in `Infrastructure` implement `IIdempotencyStore` using parameterized queries and atomic dialect constructs (`ON CONFLICT DO NOTHING`, `INSERT IGNORE`, `MERGE`).
3. **XML Documentation**:
   - Every public class, struct, record, enum, interface, method, and property must have complete XML documentation (`/// <summary>`, `<param>`, `<returns>`, `<exception>`).
4. **Header Invariant**:
   - Every source file must begin with `// Copyright © Erickson Lopez. MIT License.`

---

## 10. Git & Contribution Workflow

1. **Branch Naming**:
   - `feature/description-of-feature`
   - `fix/issue-description`
   - `refactor/component-name`
   - `docs/topic-name`
   - `perf/optimization-description`
2. **Commit Message Format**:
   Follow [Conventional Commits](https://www.conventionalcommits.org/):
   - `feat(storage): add ClickHouse persistence provider`
   - `fix(engine): handle clock skew on expired lease recovery`
   - `docs(cookbook): add distributed lock recipe`
   - `perf(hasher): optimize stackalloc span allocation`
   - `test(mediator): add cancellation token pipeline tests`
3. **Pull Request Checklist**:
   - [ ] All 31 solution projects compile cleanly with 0 warnings.
   - [ ] Unit and architecture tests pass (`dotnet test EricksonLopez.Idempotency.slnx`).
   - [ ] Native AOT compatibility verified (AOT smoke test passes).
   - [ ] Repository compliance passes (`scripts/verify-compliance.ps1`).
   - [ ] XML documentation added/updated for all public API changes.
   - [ ] Showcase updated if public API surface changed.
   - [ ] `CHANGELOG.md` updated under `[Unreleased]` if applicable.
   - [ ] Benchmark regression gate passes (if `src/**` or `benchmarks/**` were modified).

---

*Copyright © Erickson Lopez. MIT License.*
