using System.Text;
using CacheBench.Caches;

namespace CacheBench.Reporting;

public static class HtmlReport
{
    private static string Sc(double v) => v.ToString("0.000");
    private static string E(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    private static string Yn(bool b) => b ? "<span class='y'>✅</span>" : "<span class='n'>—</span>";

    public static string Build(CacheReport r, List<CacheScore> scores, List<UseCaseRanking> useCases,
        IReadOnlyList<(string Name, string Title, string Svg)> charts)
    {
        var caches = CacheRegistry.Names;
        var sb = new StringBuilder();

        sb.Append("""
<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>In-Memory Cache Benchmark</title>
<style>
  :root { color-scheme: light dark; --bg:#fff; --fg:#161a20; --muted:#667; --line:#e3e6ea; --head:#f5f7f9; --accent:#2563eb; --win:#e7f6ec; }
  @media (prefers-color-scheme: dark){ :root{ --bg:#12151b; --fg:#e7ebf0; --muted:#9aa4b2; --line:#272d38; --head:#1a1e26; --accent:#5b9bff; --win:#12351b; } }
  *{box-sizing:border-box} body{margin:0;font-family:Segoe UI,Arial,sans-serif;background:var(--bg);color:var(--fg);line-height:1.5}
  .wrap{max-width:1080px;margin:0 auto;padding:32px 20px 80px}
  h1{font-size:27px;margin:0 0 4px} h2{font-size:20px;margin:36px 0 12px;border-bottom:2px solid var(--line);padding-bottom:6px}
  .meta{color:var(--muted);font-size:14px} .meta code{background:var(--head);padding:1px 6px;border-radius:4px}
  .note{background:var(--head);border-left:3px solid var(--accent);padding:10px 14px;border-radius:4px;font-size:14px;margin:12px 0}
  .scroll{overflow-x:auto} table{border-collapse:collapse;width:100%;font-size:13px;margin:8px 0}
  th,td{border:1px solid var(--line);padding:6px 10px;text-align:right;white-space:nowrap}
  th:first-child,td:first-child{text-align:left} thead th{background:var(--head)}
  tbody tr:hover{background:var(--head)} .win{background:var(--win);font-weight:700}
  .y{color:#1a7f37} .n{color:#b0b6bf}
  figure{margin:10px 0 22px} figure svg{max-width:100%;height:auto;border:1px solid var(--line);border-radius:8px;background:#fff}
  .bar{display:inline-block;height:9px;border-radius:2px;background:var(--accent);vertical-align:middle;margin-left:6px}
  .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(240px,1fr));gap:10px}
  .card{border:1px solid var(--line);border-radius:8px;padding:12px 14px} .card h4{margin:0 0 6px;font-size:14px}
  .rank1{color:#1a7f37;font-weight:700} footer{margin-top:44px;color:var(--muted);font-size:12px}
</style></head><body><div class="wrap">
""");

        sb.Append("<h1>In-Memory Cache Benchmark</h1>");
        sb.Append($"<p class=\"meta\">MemoryCache · FusionCache · ActualLab.Fusion · LiteAPI.Cache &nbsp;|&nbsp; {E(r.Runtime)} &nbsp;|&nbsp; {E(r.Os)}<br>");
        sb.Append($"CPU: {E(r.Cpu)} ({r.LogicalCores} cores) &nbsp;|&nbsp; profile <code>{E(r.Profile)}</code> &nbsp;|&nbsp; concurrency {r.Concurrency} &nbsp;|&nbsp; {E(r.Timestamp)}</p>");
        sb.Append("<div class=\"note\">All four caches are measured through one get-or-compute surface. ActualLab.Fusion is a reactive computed-services framework (memoization + auto-invalidation), not a TTL key/value store — shown here on the same path. Scores normalized 0–1 (1 = best of four).</div>");

        // Overall score
        sb.Append("<h2>Overall Score</h2><div class=\"scroll\"><table><thead><tr><th>Rank</th><th>Cache</th><th>Hit</th><th>Throughput</th><th>Miss</th><th>Alloc</th><th>Memory</th><th>Stampede</th><th>GC pause</th><th>Overall</th></tr></thead><tbody>");
        int rank = 1;
        foreach (var s in scores)
        {
            string cls = rank == 1 ? " class=\"win\"" : "";
            sb.Append($"<tr{cls}><td>{rank++}</td><td>{E(s.Cache)}</td><td>{Sc(s.HitSpeed)}</td><td>{Sc(s.Throughput)}</td><td>{Sc(s.MissSpeed)}</td><td>{Sc(s.Alloc)}</td><td>{Sc(s.Memory)}</td><td>{Sc(s.Stampede)}</td><td>{Sc(s.GcPause)}</td><td><b>{Sc(s.Overall)}</b><span class=\"bar\" style=\"width:{(int)(s.Overall * 60)}px\"></span></td></tr>");
        }
        sb.Append("</tbody></table></div>");

        // Charts
        sb.Append("<h2>Charts</h2>");
        foreach (var (_, title, svg) in charts)
            sb.Append($"<figure aria-label=\"{E(title)}\">{svg}</figure>");

        // Feature matrix
        sb.Append("<h2>Feature Matrix</h2><div class=\"scroll\"><table><thead><tr><th>Cache</th><th>Stampede</th><th>TTL</th><th>Fail-safe</th><th>Backplane</th><th>Auto-invalidate</th><th>Async</th><th>Zero heavy deps</th></tr></thead><tbody>");
        foreach (var c in caches)
        {
            var f = r.Features(c);
            sb.Append($"<tr><td>{E(c)}</td><td>{Yn(f.StampedeProtection)}</td><td>{Yn(f.Ttl)}</td><td>{Yn(f.FailSafe)}</td><td>{Yn(f.DistributedBackplane)}</td><td>{Yn(f.AutoInvalidation)}</td><td>{Yn(f.AsyncNative)}</td><td>{Yn(f.ZeroExternalDeps)}</td></tr>");
        }
        sb.Append("</tbody></table></div>");

        // Latency & throughput
        sb.Append("<h2>Latency &amp; Throughput</h2><div class=\"scroll\"><table><thead><tr><th>Cache</th><th>Hit (get)</th><th>Miss (compute)</th><th>Set</th><th>Concurrent hit</th><th>Mixed 90/10</th><th>Hit alloc/op</th></tr></thead><tbody>");
        foreach (var c in caches)
        {
            var hit = r.Get(c, Scenarios.Hit); var miss = r.Get(c, Scenarios.Miss); var set = r.Get(c, Scenarios.Set);
            var conc = r.Get(c, Scenarios.ConcurrentHit); var mix = r.Get(c, Scenarios.Mixed);
            sb.Append($"<tr><td>{E(c)}</td><td>{Charts.Nanos(hit?.MeanNs ?? 0)}</td><td>{Charts.Nanos(miss?.MeanNs ?? 0)}</td><td>{Charts.Nanos(set?.MeanNs ?? 0)}</td><td>{Charts.Ops(conc?.OpsPerSec ?? 0)}</td><td>{Charts.Ops(mix?.OpsPerSec ?? 0)}</td><td>{Charts.Bytes(hit?.AllocBytesPerOp ?? 0)}</td></tr>");
        }
        sb.Append("</tbody></table></div>");

        // Stampede
        sb.Append($"<h2>Cache Stampede Protection</h2><p class=\"meta\">{r.StampedeConcurrency} concurrent GETs of one cold key. Factory calls = how many times the expensive work ran (1 = fully protected).</p><div class=\"scroll\"><table><thead><tr><th>Cache</th><th>Factory calls</th><th>Total time</th><th>Verdict</th></tr></thead><tbody>");
        foreach (var c in caches)
        {
            var s = r.Stamp(c); int calls = s?.FactoryCalls ?? 0;
            string verdict = calls <= 1 ? "🛡️ full" : calls >= r.StampedeConcurrency ? "❌ none" : "⚠️ partial";
            string cls = calls <= 1 ? " class=\"win\"" : "";
            sb.Append($"<tr{cls}><td>{E(c)}</td><td>{calls}</td><td>{Charts.Nanos((s?.WallMs ?? 0) * 1_000_000)}</td><td>{verdict}</td></tr>");
        }
        sb.Append("</tbody></table></div>");

        // Memory
        sb.Append($"<h2>Memory Footprint</h2><p class=\"meta\">Managed-heap growth after inserting {r.MemoryEntries:N0} entries. LiteAPI.Cache is off-heap (GC-free), so its managed/entry is near zero by design.</p><div class=\"scroll\"><table><thead><tr><th>Cache</th><th>Managed total</th><th>Managed / entry</th><th>Working-set delta</th></tr></thead><tbody>");
        foreach (var c in caches)
        {
            var m = r.Mem(c);
            sb.Append($"<tr><td>{E(c)}</td><td>{Charts.Bytes(m?.ManagedBytesTotal ?? 0)}</td><td>{Charts.Bytes(m?.ManagedBytesPerEntry ?? 0)}</td><td>{Charts.Bytes(m?.WorkingSetDelta ?? 0)}</td></tr>");
        }
        sb.Append("</tbody></table></div>");

        // GC pause
        double minMarginal = caches.Min(c => r.GcOf(c)?.MarginalPauseMs ?? 0);
        sb.Append($"<h2>GC Pause Under a Large Working Set</h2><p class=\"meta\">Forced full <b>blocking</b> GC pause with the cache empty vs. holding {r.GcEntries:N0} long-lived entries. <b>Marginal</b> = the pause the cache itself adds. <b>Retained</b> = fraction still cached afterwards — a cache with near-zero pause <i>and</i> low retention dodged the pause by evicting its entries (weak refs), so it isn't really holding the set.</p><div class=\"scroll\"><table><thead><tr><th>Cache</th><th>Managed added</th><th>Marginal pause (cache adds)</th><th>Retained after GC</th></tr></thead><tbody>");
        foreach (var c in caches)
        {
            var g = r.GcOf(c);
            double marg = g?.MarginalPauseMs ?? 0;
            double ret = g?.RetainedFraction ?? 0;
            string cls = marg <= minMarginal * 1.5 + 0.01 && ret >= 0.5 ? " class=\"win\"" : "";
            string retNote = ret < 0.5 ? $"{ret * 100:0}% ⚠️ evicts" : $"{ret * 100:0}%";
            sb.Append($"<tr{cls}><td>{E(c)}</td><td>{Charts.Bytes(g?.ManagedAddedBytes ?? 0)}</td><td><b>{marg:0.00} ms</b></td><td>{retNote}</td></tr>");
        }
        sb.Append("</tbody></table></div>");

        // Use-case rankings
        sb.Append("<h2>Use-case Rankings</h2><div class=\"grid\">");
        foreach (var uc in useCases)
        {
            sb.Append($"<div class=\"card\"><h4>{E(uc.UseCase)}</h4><ol style=\"margin:0;padding-left:18px\">");
            for (int i = 0; i < uc.Ranked.Count; i++)
            {
                string cls = i == 0 ? " class=\"rank1\"" : "";
                sb.Append($"<li{cls}>{E(uc.Ranked[i].Cache)} <span class=\"meta\">{Sc(uc.Ranked[i].Score)}</span></li>");
            }
            sb.Append("</ol></div>");
        }
        sb.Append("</div>");

        sb.Append("<footer>Generated by CacheBench · deterministic workload · custom async harness. Run <code>dotnet run -c Release -- bench</code> for BenchmarkDotNet statistics.</footer>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }
}
