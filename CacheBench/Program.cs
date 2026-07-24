using BenchmarkDotNet.Running;
using CacheBench.Benchmarks;
using CacheBench.Caches;
using CacheBench.Reporting;
using CacheBench.Workload;

// -----------------------------------------------------------------------------
// CacheBench — MemoryCache vs FusionCache vs ActualLab.Fusion vs LiteAPI.Cache.
//
//   dotnet run -c Release                 # deterministic report -> /results
//   dotnet run -c Release -- report --quick|--full
//   dotnet run -c Release -- verify       # correctness + stampede sanity
//   dotnet run -c Release -- bench [BDN args]
// -----------------------------------------------------------------------------

string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "report";

switch (mode)
{
    case "bench":
    case "benchmark":
        BenchmarkSwitcher.FromTypes(new[] { typeof(CacheBenchmarks) }).Run(args.Skip(1).ToArray(), new BenchConfig());
        break;

    case "verify":
        await VerifyAsync();
        break;

    case "trygetbench":
        CacheBench.Harness.TryGetBench.Run();
        break;

    default:
        string profile = args.Contains("--full") ? "full" : args.Contains("--quick") ? "quick" : "standard";
        Console.WriteLine($"CacheBench — report ({profile} profile)");
        Console.WriteLine("Caches: MemoryCache · FusionCache · ActualLab.Fusion · LiteAPI.Cache");
        Console.WriteLine();
        var report = await ReportRunner.RunAsync(profile);
        Output.WriteAll(report, Path.Combine(Directory.GetCurrentDirectory(), "results"));
        break;
}

static async Task VerifyAsync()
{
    Console.WriteLine("Correctness (miss+hit+miss => 2 factory calls) and stampede sanity:\n");
    bool ok = true;

    var fast = new Factory(128, 0);
    foreach (var cache in CacheRegistry.CreateAll(fast))
    {
        cache.Reset();
        fast.ResetCalls();
        var a = await cache.GetOrComputeAsync("a");
        var b = await cache.GetOrComputeAsync("a");
        _ = await cache.GetOrComputeAsync("c");
        bool good = fast.Calls == 2 && a == b;
        ok &= good;
        Console.WriteLine($"  {cache.Name,-18} factoryCalls={fast.Calls} (expect 2) roundtrip={good}");
        await cache.DisposeAsync();
    }

    Console.WriteLine("\nStampede (128 concurrent GETs of one cold key, 20ms factory):");
    foreach (var name in CacheRegistry.Names)
    {
        var slow = new Factory(128, 20);
        await using var cache = CacheRegistry.Create(name, slow);
        cache.Reset();
        slow.ResetCalls();
        await Task.WhenAll(Enumerable.Range(0, 128).Select(_ => cache.GetOrComputeAsync("hot").AsTask()));
        var f = CacheRegistry.FeaturesOf(name);
        bool matchesFeature = f.StampedeProtection ? slow.Calls <= 1 : slow.Calls > 1;
        ok &= matchesFeature;
        Console.WriteLine($"  {name,-18} factoryCalls={slow.Calls,-4} stampedeProtection={f.StampedeProtection} consistent={matchesFeature}");
    }

    Console.WriteLine();
    Console.WriteLine(ok ? "All caches behave as declared." : "Inconsistency detected — see above.");
    if (!ok) Environment.ExitCode = 1;
}
