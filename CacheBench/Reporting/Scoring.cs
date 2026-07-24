using CacheBench.Caches;

namespace CacheBench.Reporting;

public sealed record CacheScore
{
    public required string Cache { get; init; }
    public double HitSpeed { get; init; }
    public double Throughput { get; init; }
    public double MissSpeed { get; init; }
    public double Alloc { get; init; }
    public double Memory { get; init; }
    public double Stampede { get; init; }
    public double GcPause { get; init; }
    public double Overall { get; init; }
}

public sealed record UseCaseRanking
{
    public required string UseCase { get; init; }
    public required IReadOnlyList<(string Cache, double Score)> Ranked { get; init; }
    public string Winner => Ranked.Count > 0 ? Ranked[0].Cache : "-";
}

public static class Scoring
{
    // Overall weights (sum = 1.0). GC pause is a first-class latency concern, so
    // it carries the same weight as hit latency.
    private const double WHit = 0.20, WThru = 0.18, WMiss = 0.09,
                         WAlloc = 0.10, WMem = 0.10, WStamp = 0.13, WGc = 0.20;

    private static Dictionary<string, double> NormLow(Dictionary<string, double> v)
    {
        double best = v.Values.Min(), worst = v.Values.Max();
        return v.ToDictionary(k => k.Key, k => worst <= best ? 1.0 : (worst - k.Value) / (worst - best));
    }
    private static Dictionary<string, double> NormHigh(Dictionary<string, double> v)
    {
        double best = v.Values.Max(), worst = v.Values.Min();
        return v.ToDictionary(k => k.Key, k => best <= worst ? 1.0 : (k.Value - worst) / (best - worst));
    }

    private static Dictionary<string, double> Collect(CacheReport r, Func<string, double> sel)
        => CacheRegistry.Names.ToDictionary(n => n, sel);

    public static (List<CacheScore> Scores, List<UseCaseRanking> UseCases) Compute(CacheReport r)
    {
        var hit = NormLow(Collect(r, c => r.Get(c, Scenarios.Hit)?.MeanNs ?? 0));
        var thru = NormHigh(Collect(r, c => r.Get(c, Scenarios.ConcurrentHit)?.OpsPerSec ?? 0));
        var miss = NormLow(Collect(r, c => r.Get(c, Scenarios.Miss)?.MeanNs ?? 0));
        var alloc = NormLow(Collect(r, c => r.Get(c, Scenarios.Hit)?.AllocBytesPerOp ?? 0));
        var mem = NormLow(Collect(r, c => r.Mem(c)?.ManagedBytesPerEntry ?? 0));
        var stamp = NormLow(Collect(r, c => r.Stamp(c)?.FactoryCalls ?? 0));
        var gc = NormLow(Collect(r, c => r.GcOf(c)?.MarginalPauseMs ?? 0));
        var retain = NormHigh(Collect(r, c => r.GcOf(c)?.RetainedFraction ?? 0));

        var scores = CacheRegistry.Names.Select(c =>
        {
            double overall = hit[c] * WHit + thru[c] * WThru + miss[c] * WMiss
                           + alloc[c] * WAlloc + mem[c] * WMem + stamp[c] * WStamp + gc[c] * WGc;
            return new CacheScore
            {
                Cache = c, HitSpeed = hit[c], Throughput = thru[c], MissSpeed = miss[c],
                Alloc = alloc[c], Memory = mem[c], Stampede = stamp[c], GcPause = gc[c], Overall = overall,
            };
        }).OrderByDescending(s => s.Overall).ToList();

        // Use-case weights over:
        // [hit, throughput, miss, alloc, memory, gcPause, retention, stampede, ttl, failsafe, backplane, autoInval]
        // "retention" only matters where the cache must actually HOLD a large working
        // set — it disqualifies caches that dodge GC pause by evicting (weak refs).
        (string name, double[] w)[] cases =
        {
            ("Read-heavy API cache",           new[] { .30, .30, .05, .10, .05, .00, .00, .10, .10, .00, .00, .00 }),
            ("High-concurrency / stampede",    new[] { .10, .35, .10, .10, .00, .00, .00, .35, .00, .00, .00, .00 }),
            ("Microservice w/ TTL",            new[] { .20, .20, .10, .00, .00, .00, .00, .10, .25, .15, .00, .00 }),
            ("Low-memory / IoT / embedded",    new[] { .15, .10, .00, .20, .25, .30, .00, .00, .00, .00, .00, .00 }),
            ("Simple in-process cache",        new[] { .35, .25, .05, .20, .15, .00, .00, .00, .00, .00, .00, .00 }),
            ("Distributed-ready",              new[] { .15, .20, .00, .00, .00, .00, .00, .10, .15, .00, .40, .00 }),
            ("Resilience / fail-safe",         new[] { .10, .10, .00, .00, .00, .00, .00, .25, .20, .35, .00, .00 }),
            ("Reactive / real-time",           new[] { .15, .20, .00, .00, .00, .00, .00, .20, .00, .00, .00, .45 }),
            ("GC-sensitive / large working set", new[] { .05, .05, .00, .00, .20, .35, .30, .05, .00, .00, .00, .00 }),
        };

        var useCases = cases.Select(uc =>
        {
            var ranked = CacheRegistry.Names.Select(c =>
            {
                var f = CacheRegistry.FeaturesOf(c);
                double[] d =
                {
                    hit[c], thru[c], miss[c], alloc[c], mem[c], gc[c], retain[c], stamp[c],
                    f.Ttl ? 1 : 0, f.FailSafe ? 1 : 0, f.DistributedBackplane ? 1 : 0, f.AutoInvalidation ? 1 : 0,
                };
                double score = 0;
                for (int i = 0; i < uc.w.Length; i++) score += d[i] * uc.w[i];
                return (Cache: c, Score: score);
            }).OrderByDescending(x => x.Score).ToList();
            return new UseCaseRanking { UseCase = uc.name, Ranked = ranked };
        }).ToList();

        return (scores, useCases);
    }
}
