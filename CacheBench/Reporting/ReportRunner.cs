using System.Runtime.InteropServices;
using CacheBench.Caches;
using CacheBench.Harness;
using CacheBench.Workload;

namespace CacheBench.Reporting;

public static class ReportRunner
{
    private sealed record Plan(
        int WorkingSet, int HitIters, int MissIters, int SetIters,
        int ConcurrentTotal, int MixedTotal, int Concurrency,
        int StampedeConcurrency, int StampedeDelayMs, int MemoryEntries, int ValueBytes,
        int GcEntries);

    private static Plan For(string profile)
    {
        int cores = Environment.ProcessorCount;
        return profile switch
        {
            "quick" => new Plan(2_000, 30_000, 5_000, 20_000, 100_000, 100_000, cores, 128, 20, 20_000, 256, 50_000),
            "full" => new Plan(20_000, 1_000_000, 200_000, 500_000, 5_000_000, 5_000_000, cores, 512, 25, 200_000, 256, 1_000_000),
            _ => new Plan(10_000, 300_000, 50_000, 200_000, 1_000_000, 1_000_000, cores, 256, 20, 100_000, 256, 300_000), // standard
        };
    }

    public static async Task<CacheReport> RunAsync(string profile)
    {
        var plan = For(profile);
        var report = new CacheReport
        {
            Timestamp = DateTime.UtcNow.ToString("u"),
            Runtime = RuntimeInformation.FrameworkDescription,
            Os = RuntimeInformation.OSDescription,
            Cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? RuntimeInformation.ProcessArchitecture.ToString(),
            LogicalCores = Environment.ProcessorCount,
            Profile = profile,
            Concurrency = plan.Concurrency,
            StampedeConcurrency = plan.StampedeConcurrency,
            MemoryEntries = plan.MemoryEntries,
            GcEntries = plan.GcEntries,
        };

        var keys = Keys.Generate(Math.Max(Math.Max(plan.WorkingSet, plan.MemoryEntries), plan.GcEntries));
        const string payload = "cache-value-payload";

        foreach (var name in CacheRegistry.Names)
        {
            Console.WriteLine($"[report] {name} ...");

            // ---- latency / throughput scenarios (fast factory) ----
            var fast = new Factory(plan.ValueBytes, delayMs: 0);
            await using (var cache = CacheRegistry.Create(name, fast))
            {
                cache.Reset();
                // warm the whole working set so Hit/Concurrent/Mixed are true hits
                for (int i = 0; i < plan.WorkingSet; i++)
                    await cache.GetOrComputeAsync(keys[i]);

                int ws = plan.WorkingSet;
                report.Results.Add(await Runner.MeasureAsync(name, Scenarios.Hit,
                    i => cache.GetOrComputeAsync(keys[i % ws]), warmup: 100, iterations: plan.HitIters));

                report.Results.Add(await Runner.MeasureConcurrentAsync(name, Scenarios.ConcurrentHit,
                    i => cache.GetOrComputeAsync(keys[i % ws]), plan.Concurrency, plan.ConcurrentTotal));

                report.Results.Add(await Runner.MeasureConcurrentAsync(name, Scenarios.Mixed,
                    async i =>
                    {
                        var k = keys[i % ws];
                        if (i % 10 == 0) { await cache.SetAsync(k, payload); return payload; }
                        return await cache.GetOrComputeAsync(k);
                    }, plan.Concurrency, plan.MixedTotal));

                report.Results.Add(await Runner.MeasureAsync(name, Scenarios.Set,
                    async i => { await cache.SetAsync(keys[i % ws], payload); return payload; },
                    warmup: 100, iterations: plan.SetIters));
            }

            // ---- miss scenario: each op a brand-new key (fresh adapter) ----
            var missFactory = new Factory(plan.ValueBytes, delayMs: 0);
            await using (var cache = CacheRegistry.Create(name, missFactory))
            {
                cache.Reset();
                report.Results.Add(await Runner.MeasureAsync(name, Scenarios.Miss,
                    i => cache.GetOrComputeAsync("miss:" + i), warmup: 100, iterations: plan.MissIters));
            }

            // ---- stampede: many concurrent GETs of one cold key (slow factory) ----
            var slow = new Factory(plan.ValueBytes, delayMs: plan.StampedeDelayMs);
            await using (var cache = CacheRegistry.Create(name, slow))
            {
                report.Stampede.Add(await Runner.MeasureStampedeAsync(name, cache, slow, plan.StampedeConcurrency));
            }

            // ---- memory footprint of N entries (fresh adapter) ----
            var memFactory = new Factory(plan.ValueBytes, delayMs: 0);
            await using (var cache = CacheRegistry.Create(name, memFactory))
            {
                report.Memory.Add(await Runner.MeasureMemoryAsync(name, cache, keys, plan.MemoryEntries));
            }

            // ---- GC pause while holding a large long-lived working set ----
            var gcFactory = new Factory(plan.ValueBytes, delayMs: 0);
            await using (var cache = CacheRegistry.Create(name, gcFactory))
            {
                report.Gc.Add(await Runner.MeasureGcPauseAsync(name, cache, gcFactory, keys, plan.GcEntries));
            }
        }

        return report;
    }
}
