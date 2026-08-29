# Testing Strategy & Test Suites

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Multi-Tier Testing Pyramid

The test suite in `dotnet-idempotency` is divided into three distinct levels:

```text
               ┌────────────────────────┐
               │ Native AOT & Smoke     │ (Self-contained Linux-x64 binary execution)
               ├────────────────────────┤
               │   Integration Tests    │ (100 Concurrent Threads,
               │                        │  Lease Stealing, SQLite/PG/Redis)
               ├────────────────────────┤
               │   Architecture Tests   │ (Clean Architecture boundaries,
               │                        │  Purity, Immutability)
               ├────────────────────────┤
               │       Unit Tests       │ (Core Engine, Fingerprinting,
               │                        │  Value Objects, Adapters)
               └────────────────────────┘
```

---

## 2. Test Suites Catalog

The solution contains 16 specialized test projects ensuring comprehensive verification:

| Test Project | Framework / Tools | Scope & Responsibility |
|---|---|---|
| `EricksonLopez.Idempotency.Abstractions.Tests` | xUnit, AwesomeAssertions | Value object validation (`IdempotencyKey`, `IdempotencyScope`), length limits, struct immutability. |
| `EricksonLopez.Idempotency.Tests` | xUnit, AwesomeAssertions, NSubstitute | Core state transitions, fingerprint mismatch rejections, engine execution workflows. |
| `EricksonLopez.Idempotency.ArchitectureTests` | NetArchTest.Rules | Verifies that Domain/Abstractions have zero infrastructure dependencies and adapters remain isolated. |
| `EricksonLopez.Idempotency.AspNetCore.Tests` | xUnit, Microsoft.AspNetCore.Http | Endpoint filters, middleware stream capture, problem details RFC 9110 mapping. |
| `EricksonLopez.Idempotency.Mediator.Tests` | xUnit, EricksonLopez.Mediator | Pipeline behavior command interception, multi-tenant CQRS isolation, and result caching. |
| `EricksonLopez.Idempotency.Result.Tests` | xUnit, EricksonLopez.Result | Functional domain error factory mappings (`IdempotencyErrors`, `AsErrorResult<T>()`). |
| `EricksonLopez.Idempotency.Testing.Tests` | xUnit, AwesomeAssertions | In-memory concurrent store test double, simulated leases, and CAS versioning. |
| `EricksonLopez.Idempotency.PostgreSql.Tests` | xUnit, Npgsql, Dapper | PostgreSQL store dialect tests, parameterized SQL generation, `ON CONFLICT` and transactional participation. |
| `EricksonLopez.Idempotency.SqlServer.Tests` | xUnit, SqlClient, Dapper | SQL Server store dialect tests, `MERGE WITH (HOLDLOCK)`, and transactional participation. |
| `EricksonLopez.Idempotency.MySql.Tests` | xUnit, MySqlConnector, Dapper | MySQL store dialect tests, `INSERT IGNORE INTO`, and lease stealing logic. |
| `EricksonLopez.Idempotency.MariaDb.Tests` | xUnit, MySqlConnector, Dapper | MariaDB store dialect tests, `INSERT IGNORE INTO`, and lease stealing logic. |
| `EricksonLopez.Idempotency.Oracle.Tests` | xUnit, OracleClient, Dapper | Oracle Database store dialect tests, `MERGE INTO`, and lease stealing logic. |
| `EricksonLopez.Idempotency.Sqlite.Tests` | xUnit, Microsoft.Data.Sqlite | SQLite embedded database store tests, `INSERT OR IGNORE INTO`, and transactional execution. |
| `EricksonLopez.Idempotency.Redis.Tests` | xUnit, StackExchange.Redis | Redis storage adapter tests, atomic Lua script execution, CAS state transitions, and TTL verification. |
| `EricksonLopez.Idempotency.IntegrationTests` | xUnit, Multithreading | High-concurrency race condition tests (100 concurrent tasks on same key: exactly 1 executes, 99 receive conflict/replay). |
| `EricksonLopez.Idempotency.AotSmokeTest` | Native AOT Executable | Compiles and executes in Native AOT mode on Linux x64 to verify zero runtime reflection or trimming crashes. |

---

## 3. Running All Tests

```bash
# Run all tests in the solution
dotnet test EricksonLopez.Idempotency.slnx --configuration Release

# Run only architecture tests
dotnet test tests/EricksonLopez.Idempotency.ArchitectureTests/EricksonLopez.Idempotency.ArchitectureTests.csproj

# Run integration tests
dotnet test tests/EricksonLopez.Idempotency.IntegrationTests/EricksonLopez.Idempotency.IntegrationTests.csproj

# Run Native AOT smoke test
dotnet test tests/EricksonLopez.Idempotency.AotSmokeTest/EricksonLopez.Idempotency.AotSmokeTest.csproj
```

---

## 4. `InMemoryIdempotencyStore.Clear()` — Test Isolation

`InMemoryIdempotencyStore` exposes a `Clear()` method for resetting all stored idempotency records
between test cases without re-instantiating the store:

```csharp
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    // ... standard IIdempotencyStore members ...

    /// <summary>
    /// Removes all stored idempotency records from this in-memory store.
    /// Use this in test teardown to reset store state between test cases.
    /// </summary>
    public void Clear() { /* ... */ }
}
```

**Usage pattern with xUnit:**

```csharp
public class PaymentServiceTests : IDisposable
{
    private readonly InMemoryIdempotencyStore _store = new();

    [Fact]
    public async Task CreatePayment_WithSameKey_ReturnsCachedResponse()
    {
        // Arrange
        var engine = new IdempotencyEngine(_store, ...);

        // Act — first call
        var result1 = await engine.ExecuteAsync(...);

        // Act — duplicate call with same idempotency key
        var result2 = await engine.ExecuteAsync(...);

        // Assert — both return same result
        result2.Should().BeEquivalentTo(result1);
    }

    public void Dispose()
    {
        // Reset store state between tests
        _store.Clear();
    }
}
```

> [!TIP]
> When using `InMemoryIdempotencyStore` as a shared singleton across test classes (e.g., via DI),
> call `Clear()` in `IAsyncLifetime.InitializeAsync()` or `Dispose()` to prevent state leakage
> between test cases.
