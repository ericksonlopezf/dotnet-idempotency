// Copyright © Erickson Lopez. MIT License.
using BenchmarkDotNet.Running;

namespace EricksonLopez.Idempotency.Benchmarks;

/// <summary>
/// Provides the entry point for executing BenchmarkDotNet benchmark suites.
/// </summary>
public static class Program
{
    /// <summary>
    /// Executes the benchmark suite.
    /// </summary>
    /// <param name="args">The command-line arguments passed to the application.</param>
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<FingerprintHasherBenchmarks>();
    }
}
