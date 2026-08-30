```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 3.21GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.400
  [Host] : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3


```
| Method                  | Mean | Error | Ratio | RatioSD | Alloc Ratio |
|------------------------ |-----:|------:|------:|--------:|------------:|
| ComputeFingerprintSmall |   NA |    NA |     ? |       ? |           ? |

Benchmarks with issues:
  FingerprintHasherBenchmarks.ComputeFingerprintSmall: DefaultJob
