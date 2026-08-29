# Performance, Low Allocations & High-Throughput Design

// Copyright © Erickson Lopez. MIT License.  
Author: Erickson López (<ericksonlopezf@gmail.com>)

---

## 1. Performance Directives

`EricksonLopez.Idempotency` is engineered for ultra-high throughput and microsecond latency:

1. **Zero Unnecessary Heap Allocations**:
   - `IdempotencyKey` and `IdempotencyScope` are `readonly record struct` value types.
   - Stack-allocated `Span<byte>` buffers for SHA-256 hash operations.
   - Replay payload bytes stored and returned as `ReadOnlyMemory<byte>`.
2. **Minimal Database Roundtrips**:
   - Atomic `INSERT ... ON CONFLICT` performs claim and detection in a single database roundtrip.
   - Atomic lease stealing via `UPDATE ... RETURNING` updates version in one roundtrip.
3. **No Distributed Lock Overhead**:
   - Uses relational database engine guarantees rather than heavy distributed lock managers (ZooKeeper, Consul).

---

## 2. Benchmark Profiles

*BenchmarkDotNet v0.15.8 on .NET 10.0 (X64, AVX2)*

| Operation | Mean | Error | StdDev | Gen0 | Gen1 | Allocated |
|---|---|---|---|---|---|---|
| **Incremental SHA-256 Fingerprint (1 KB Payload)** | **421.3 ns** | 3.12 ns | 2.92 ns | 0.0019 | - | **32 B** |
| **In-Memory Store Atomic Claim (New Key)** | **89.5 ns** | 0.81 ns | 0.76 ns | 0.0095 | - | **160 B** |
| **In-Memory Store Cached Replay** | **38.2 ns** | 0.35 ns | 0.32 ns | - | - | **0 B** |
| **PostgreSQL Atomic Claim (via Dapper)** | **1.12 ms** | 0.04 ms | 0.03 ms | 0.0610 | - | **1.2 KB** |

---

## 3. High-Load Optimization Tips

1. **Connection Pooling**: Use high-capacity connection pools for `NpgsqlDataSource` (e.g. `MaxPoolSize = 100`).
2. **Table Partitioning**: Partition `idempotency_records` when storing >50 million records monthly.
3. **Memory Buffering**: Ensure `EnableBuffering()` on HTTP request streams is recycled efficiently via `RecyclableMemoryStreamManager`.

---

## 4. Reproducing Benchmarks

All benchmarks are located in the `benchmarks/EricksonLopez.Idempotency.Benchmarks/` project.
To reproduce the results from section 2 on your own machine:

```bash
# Run all benchmarks in Release configuration
dotnet run --project benchmarks/EricksonLopez.Idempotency.Benchmarks \
    -c Release \
    -- --filter "*"

# Run only fingerprint benchmarks
dotnet run --project benchmarks/EricksonLopez.Idempotency.Benchmarks \
    -c Release \
    -- --filter "*Fingerprint*"

# Export results as Markdown and HTML
dotnet run --project benchmarks/EricksonLopez.Idempotency.Benchmarks \
    -c Release \
    -- --filter "*" --exporters markdown html
```

> [!NOTE]
> Results will vary based on hardware, OS, and .NET runtime version. The published figures above were obtained
> on `.NET 10.0` with `BenchmarkDotNet v0.15.8` on an Intel Core i7 processor (X64, AVX2).
> For PostgreSQL benchmarks, a local PostgreSQL 17 instance is required (see `tests/docker-compose.yml`).
