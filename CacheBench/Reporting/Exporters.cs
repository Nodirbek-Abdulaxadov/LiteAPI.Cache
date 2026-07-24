using System.Globalization;
using System.Text;
using CacheBench.Caches;

namespace CacheBench.Reporting;

public static class Exporters
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string Sc(double v) => v.ToString("0.000", Inv);
    private static string Yn(bool b) => b ? "✅" : "—";

    public static string BuildMarkdown(CacheReport r, List<CacheScore> scores, List<UseCaseRanking> useCases)
    {
        var sb = new StringBuilder();
        var caches = CacheRegistry.Names;

        sb.AppendLine("# In-Memory Cache Benchmark");
        sb.AppendLine();
        sb.AppendLine($"- **Generated:** {r.Timestamp}");
        sb.AppendLine($"- **Runtime:** {r.Runtime}");
        sb.AppendLine($"- **OS:** {r.Os}");
        sb.AppendLine($"- **CPU:** {r.Cpu} ({r.LogicalCores} logical cores)");
        sb.AppendLine($"- **Profile:** {r.Profile} · concurrency {r.Concurrency} · stampede {r.StampedeConcurrency} · memory {r.MemoryEntries:N0} entries");
        sb.AppendLine($"- **Caches:** MemoryCache, FusionCache (ZiggyCreatures), ActualLab.Fusion, LiteAPI.Cache (JustCache)");
        sb.AppendLine();
        sb.AppendLine("> Warmup: 100 iterations per benchmark. All caches measured through a common get-or-compute surface.");
        sb.AppendLine("> ActualLab.Fusion is a reactive *computed-services* framework (memoization + auto-invalidation), not a TTL store — compared here on the same get-or-compute path.");
        sb.AppendLine();

        // Overall score
        sb.AppendLine("## Overall Score");
        sb.AppendLine();
        sb.AppendLine("Normalized 0–1 (1 = best of four). Overall = weighted blend.");
        sb.AppendLine();
        sb.AppendLine("| Rank | Cache | Hit | Throughput | Miss | Alloc | Memory | Stampede | GC pause | **Overall** |");
        sb.AppendLine("|-----:|-------|----:|-----------:|-----:|------:|-------:|---------:|---------:|------------:|");
        int rank = 1;
        foreach (var s in scores)
            sb.AppendLine($"| {rank++} | {s.Cache} | {Sc(s.HitSpeed)} | {Sc(s.Throughput)} | {Sc(s.MissSpeed)} | {Sc(s.Alloc)} | {Sc(s.Memory)} | {Sc(s.Stampede)} | {Sc(s.GcPause)} | **{Sc(s.Overall)}** |");
        sb.AppendLine();

        // Feature matrix
        sb.AppendLine("## Feature Matrix");
        sb.AppendLine();
        sb.AppendLine("| Cache | Stampede protection | TTL | Fail-safe | Distributed backplane | Auto-invalidation | Async-native | Zero heavy deps |");
        sb.AppendLine("|-------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|");
        foreach (var c in caches)
        {
            var f = r.Features(c);
            sb.AppendLine($"| {c} | {Yn(f.StampedeProtection)} | {Yn(f.Ttl)} | {Yn(f.FailSafe)} | {Yn(f.DistributedBackplane)} | {Yn(f.AutoInvalidation)} | {Yn(f.AsyncNative)} | {Yn(f.ZeroExternalDeps)} |");
        }
        sb.AppendLine();

        // Latency & throughput
        sb.AppendLine("## Latency & Throughput");
        sb.AppendLine();
        sb.AppendLine("| Cache | Hit (get) | Miss (compute) | Set | Concurrent hit | Mixed 90/10 | Hit alloc/op |");
        sb.AppendLine("|-------|----------:|---------------:|----:|---------------:|------------:|-------------:|");
        foreach (var c in caches)
        {
            var hit = r.Get(c, Scenarios.Hit);
            var miss = r.Get(c, Scenarios.Miss);
            var set = r.Get(c, Scenarios.Set);
            var conc = r.Get(c, Scenarios.ConcurrentHit);
            var mix = r.Get(c, Scenarios.Mixed);
            sb.AppendLine($"| {c} | {Charts.Nanos(hit?.MeanNs ?? 0)} | {Charts.Nanos(miss?.MeanNs ?? 0)} | {Charts.Nanos(set?.MeanNs ?? 0)} | {Charts.Ops(conc?.OpsPerSec ?? 0)} | {Charts.Ops(mix?.OpsPerSec ?? 0)} | {Charts.Bytes(hit?.AllocBytesPerOp ?? 0)} |");
        }
        sb.AppendLine();

        // Stampede
        sb.AppendLine("## Cache Stampede Protection");
        sb.AppendLine();
        sb.AppendLine($"{r.StampedeConcurrency} concurrent GETs of one cold key (slow factory). Factory calls = how many times the expensive work actually ran (1 = fully protected).");
        sb.AppendLine();
        sb.AppendLine("| Cache | Factory calls | Total time | Verdict |");
        sb.AppendLine("|-------|--------------:|-----------:|---------|");
        foreach (var c in caches)
        {
            var s = r.Stamp(c);
            int calls = s?.FactoryCalls ?? 0;
            string verdict = calls <= 1 ? "🛡️ full protection" : calls >= r.StampedeConcurrency ? "❌ no protection" : "⚠️ partial";
            sb.AppendLine($"| {c} | {calls} | {Charts.Nanos(s?.WallMs * 1_000_000 ?? 0)} | {verdict} |");
        }
        sb.AppendLine();

        // Memory
        sb.AppendLine("## Memory Footprint");
        sb.AppendLine();
        sb.AppendLine($"Managed-heap growth after inserting {r.MemoryEntries:N0} entries. LiteAPI.Cache stores values off-heap (GC-free), so its managed/entry is near zero by design.");
        sb.AppendLine();
        sb.AppendLine("| Cache | Managed total | Managed / entry | Working-set delta |");
        sb.AppendLine("|-------|--------------:|----------------:|------------------:|");
        foreach (var c in caches)
        {
            var m = r.Mem(c);
            sb.AppendLine($"| {c} | {Charts.Bytes(m?.ManagedBytesTotal ?? 0)} | {Charts.Bytes(m?.ManagedBytesPerEntry ?? 0)} | {Charts.Bytes(m?.WorkingSetDelta ?? 0)} |");
        }
        sb.AppendLine();

        // GC pause
        sb.AppendLine("## GC Pause Under a Large Working Set");
        sb.AppendLine();
        sb.AppendLine($"Forced full **blocking** GC pause measured with the cache empty vs. holding {r.GcEntries:N0} long-lived entries. **Marginal** = the pause the cache itself adds (empty and full share the same key array + process state, which cancels). Off-heap caches add almost nothing to the managed graph.");
        sb.AppendLine();
        sb.AppendLine("| Cache | Managed added | **Marginal pause (cache adds)** | Retained after GC |");
        sb.AppendLine("|-------|--------------:|-------------------------------:|------------------:|");
        foreach (var c in caches)
        {
            var g = r.GcOf(c);
            double ret = g?.RetainedFraction ?? 0;
            string retNote = ret < 0.5 ? $"{ret * 100:0}% ⚠️ evicts" : $"{ret * 100:0}%";
            sb.AppendLine($"| {c} | {Charts.Bytes(g?.ManagedAddedBytes ?? 0)} | **{g?.MarginalPauseMs:0.00} ms** | {retNote} |");
        }
        sb.AppendLine();
        sb.AppendLine("*Retained = fraction of the working set still cached after the GCs. A cache that shows near-zero GC pause **and** low retention dodged the pause by evicting its entries (e.g. weak references) — it isn't actually holding the working set.*");
        sb.AppendLine();

        // Use-case rankings
        sb.AppendLine("## Use-case Rankings");
        sb.AppendLine();
        sb.AppendLine("Weighted blend of measured metrics + declared features. Higher = better fit.");
        sb.AppendLine();
        sb.AppendLine("| Use case | 1st | 2nd | 3rd | 4th |");
        sb.AppendLine("|----------|-----|-----|-----|-----|");
        foreach (var uc in useCases)
        {
            string Cell(int i) => i < uc.Ranked.Count ? $"{uc.Ranked[i].Cache} ({Sc(uc.Ranked[i].Score)})" : "-";
            sb.AppendLine($"| {uc.UseCase} | **{Cell(0)}** | {Cell(1)} | {Cell(2)} | {Cell(3)} |");
        }
        sb.AppendLine();

        return sb.ToString();
    }

    public static string BuildResultsCsv(CacheReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Cache,Scenario,Iterations,Concurrency,MeanNs,OpsPerSec,AllocBytesPerOp,Gen0,Gen1,Gen2,WallMs,FactoryCalls");
        foreach (var m in r.Results.Concat(r.Stampede))
            sb.AppendLine(string.Join(",", m.Cache, Q(m.Scenario), m.Iterations, m.Concurrency,
                F(m.MeanNs), F(m.OpsPerSec), m.AllocBytesPerOp, m.Gen0, m.Gen1, m.Gen2, F(m.WallMs), m.FactoryCalls));
        return sb.ToString();
    }

    public static string BuildMemoryCsv(CacheReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Cache,Entries,ManagedBytesTotal,ManagedBytesPerEntry,WorkingSetDelta");
        foreach (var m in r.Memory)
            sb.AppendLine(string.Join(",", m.Cache, m.Entries, m.ManagedBytesTotal, F(m.ManagedBytesPerEntry), m.WorkingSetDelta));
        return sb.ToString();
    }

    public static string BuildGcCsv(CacheReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Cache,Entries,ManagedAddedBytes,EmptyPauseMs,FullPauseMs,MarginalPauseMs,RetainedFraction");
        foreach (var g in r.Gc)
            sb.AppendLine(string.Join(",", g.Cache, g.Entries, g.ManagedAddedBytes, F(g.EmptyPauseMs), F(g.FullPauseMs), F(g.MarginalPauseMs), F(g.RetainedFraction)));
        return sb.ToString();
    }

    public static string BuildFeaturesCsv(CacheReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Cache,StampedeProtection,Ttl,FailSafe,DistributedBackplane,AutoInvalidation,AsyncNative,ZeroExternalDeps");
        foreach (var c in CacheRegistry.Names)
        {
            var f = r.Features(c);
            sb.AppendLine(string.Join(",", c, f.StampedeProtection, f.Ttl, f.FailSafe, f.DistributedBackplane, f.AutoInvalidation, f.AsyncNative, f.ZeroExternalDeps));
        }
        return sb.ToString();
    }

    private static string F(double v) => v.ToString("0.####", Inv);
    private static string Q(string s) => s.Contains(',') ? $"\"{s}\"" : s;
}
