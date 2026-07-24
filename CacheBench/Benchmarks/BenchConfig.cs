using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;

namespace CacheBench.Benchmarks;

public sealed class BenchConfig : ManualConfig
{
    public BenchConfig()
    {
        AddJob(Job.Default
            .WithWarmupCount(100)
            .WithIterationCount(15)
            .WithLaunchCount(1)
            .WithId("cache"));

        AddDiagnoser(MemoryDiagnoser.Default);
        AddLogger(ConsoleLogger.Default);
        AddColumnProvider(DefaultColumnProviders.Instance);

        // Markdown/CSV/HTML exporters come from BenchmarkDotNet's default config.
        WithOptions(ConfigOptions.DisableOptimizationsValidator);
    }
}
