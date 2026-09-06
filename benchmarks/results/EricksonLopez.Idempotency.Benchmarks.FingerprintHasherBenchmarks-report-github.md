```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Platinum 8370C CPU 2.80GHz (Max: 3.40GHz), 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.400
  [Host] : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v4


```
| Method                  | Mean | Error | Ratio | RatioSD | Alloc Ratio |
|------------------------ |-----:|------:|------:|--------:|------------:|
| ComputeFingerprintSmall |   NA |    NA |     ? |       ? |           ? |

Benchmarks with issues:
  FingerprintHasherBenchmarks.ComputeFingerprintSmall: DefaultJob
