using CacheBench.Caches;

namespace CacheBench.Reporting;

public static class Output
{
    public static void WriteAll(CacheReport report, string outDir)
    {
        Directory.CreateDirectory(outDir);
        string chartsDir = Path.Combine(outDir, "charts");
        Directory.CreateDirectory(chartsDir);

        var (scores, useCases) = Scoring.Compute(report);

        var charts = new (string Name, string Title, string Svg)[]
        {
            ("hit-latency", "Hit latency (warm get)",
                Charts.Metric("Hit latency (warm get)", c => report.Get(c, Scenarios.Hit)?.MeanNs ?? 0, Charts.Nanos, lowerBetter: true)),
            ("throughput", "Concurrent hit throughput",
                Charts.Metric("Concurrent hit throughput", c => report.Get(c, Scenarios.ConcurrentHit)?.OpsPerSec ?? 0, Charts.Ops, lowerBetter: false)),
            ("stampede", "Cache stampede — factory calls",
                Charts.Metric("Cache stampede — factory calls (lower = better protection)", c => report.Stamp(c)?.FactoryCalls ?? 0, v => v.ToString("0"), lowerBetter: true)),
            ("memory-per-entry", "Managed memory per entry",
                Charts.Metric("Managed memory per entry", c => report.Mem(c)?.ManagedBytesPerEntry ?? 0, Charts.Bytes, lowerBetter: true)),
            ("mixed-throughput", "Mixed 90/10 throughput",
                Charts.Metric("Mixed 90/10 throughput", c => report.Get(c, Scenarios.Mixed)?.OpsPerSec ?? 0, Charts.Ops, lowerBetter: false)),
            ("gc-pause", "Marginal GC pause under a large working set",
                Charts.Metric($"Marginal full-GC pause added by holding {report.GcEntries:N0} entries",
                    c => report.GcOf(c)?.MarginalPauseMs ?? 0, v => $"{v:0.00} ms", lowerBetter: true)),
        };

        foreach (var (name, _, svg) in charts)
            File.WriteAllText(Path.Combine(chartsDir, $"{name}.svg"), svg);

        File.WriteAllText(Path.Combine(outDir, "cache-report.md"), Exporters.BuildMarkdown(report, scores, useCases));
        File.WriteAllText(Path.Combine(outDir, "cache-report.html"), HtmlReport.Build(report, scores, useCases, charts));
        File.WriteAllText(Path.Combine(outDir, "results.csv"), Exporters.BuildResultsCsv(report));
        File.WriteAllText(Path.Combine(outDir, "memory.csv"), Exporters.BuildMemoryCsv(report));
        File.WriteAllText(Path.Combine(outDir, "gc.csv"), Exporters.BuildGcCsv(report));
        File.WriteAllText(Path.Combine(outDir, "features.csv"), Exporters.BuildFeaturesCsv(report));

        Console.WriteLine();
        Console.WriteLine($"Artifacts written to: {outDir}");
        Console.WriteLine("  cache-report.md / .html · results.csv · memory.csv · gc.csv · features.csv · charts/*.svg (6)");
        Console.WriteLine();
        Console.WriteLine($"Marginal GC pause added by holding {report.GcEntries:N0} entries:");
        foreach (var c in CacheRegistry.Names)
        {
            var g = report.GcOf(c);
            double ret = g?.RetainedFraction ?? 0;
            string retNote = ret < 0.5 ? $", retained {ret * 100:0}% (evicts!)" : $", retained {ret * 100:0}%";
            Console.WriteLine($"  {c,-18} +{g?.MarginalPauseMs,6:0.00} ms  (managed +{Charts.Bytes(g?.ManagedAddedBytes ?? 0)}{retNote})");
        }
        Console.WriteLine();
        Console.WriteLine("Overall ranking:");
        int rk = 1;
        foreach (var s in scores)
            Console.WriteLine($"  {rk++}. {s.Cache,-18} score={s.Overall:0.000}");
    }
}
