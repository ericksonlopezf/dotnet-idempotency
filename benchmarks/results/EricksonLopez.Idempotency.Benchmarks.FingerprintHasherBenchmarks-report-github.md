```

BenchmarkDotNet v0.15.8, Linux Ubuntu 26.04.1 LTS (Resolute Raccoon)
AMD EPYC 9V45 4.41GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 11.0.100
  [Host] : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v4


```
| Method                  | Mean | Error | Ratio | RatioSD | Alloc Ratio |
|------------------------ |-----:|------:|------:|--------:|------------:|
| ComputeFingerprintSmall |   NA |    NA |     ? |       ? |           ? |

Benchmarks with issues:
  FingerprintHasherBenchmarks.ComputeFingerprintSmall: DefaultJob
