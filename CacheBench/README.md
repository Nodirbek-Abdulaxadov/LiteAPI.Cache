# CacheBench

A comprehensive, **deterministic** benchmark comparing four .NET in-memory caches on **.NET 10 / C#**:

| Cache | Package | Model |
|-------|---------|-------|
| MemoryCache | `Microsoft.Extensions.Caching.Memory` (in-box) | classic key/value + TTL |
| FusionCache | `ZiggyCreatures.FusionCache` | batteries-included (stampede, fail-safe, backplane) |
| ActualLab.Fusion | `ActualLab.Fusion` | reactive *computed services* (memoize + auto-invalidate) |
| LiteAPI.Cache | `LiteAPI.Cache` (`JustCache`) | low-level, off-heap, Redis-like, single-flight get-or-compute |

All four are measured through **one common `get-or-compute` surface** (`Caches/ICacheAdapter.cs`) —
each via *its own native* get-or-compute, so stampede protection is exercised fairly and the
comparison stays apples-to-apples even though the libraries have very different designs.

## Quick start

```bash
dotnet run -c Release
```

Runs the deterministic **report** and writes everything to [`results/`](results/):

```
results/
  cache-report.md        # all tables + rankings
  cache-report.html      # styled, inline charts, light/dark
  results.csv            # every scenario measurement
  memory.csv             # footprint per cache
  features.csv           # feature matrix
  charts/                # hit-latency, throughput, stampede, memory-per-entry, mixed-throughput (SVG)
```

### Modes

```bash
dotnet run -c Release                 # standard report -> results/
dotnet run -c Release -- report --quick   # faster, smaller counts
dotnet run -c Release -- report --full    # heavier tiers
dotnet run -c Release -- verify           # correctness + stampede sanity per cache
dotnet run -c Release -- bench            # BenchmarkDotNet per-op statistics
dotnet run -c Release -- bench --filter *Hit*
```

## What is measured

**Metrics:** hit latency (warm get), miss latency (get-or-compute), set latency,
single-thread throughput, concurrent throughput, mixed 90/10 read-write throughput,
per-op allocations, GC collections, **cache-stampede protection** (factory-call count
under concurrent misses), **memory footprint** (managed heap + working set for N entries),
and **GC pause + retention under a large working set** (marginal full-GC pause the cache
adds, and how much of the set it still holds afterwards).

**Scenarios** (`Harness/Runner.cs`, `Reporting/ReportRunner.cs`):

- **Hit** — warm get over a working set (all hits)
- **Miss** — get-or-compute with a brand-new key every op
- **Set** — repeated writes
- **Concurrent hit** — N workers hammering the working set
- **Mixed 90/10** — realistic read-heavy workload, concurrent
- **Stampede** — many concurrent GETs of one cold key → how many times the factory runs
- **Memory** — managed + working-set growth after inserting N entries
- **GC pause** — forced full blocking GC with the cache empty vs. holding N entries
  (the delta isolates the cache's own cost), plus **retention** (how much is still cached
  afterwards — catches caches that dodge GC by evicting)

**Rankings** — an overall weighted score plus per-use-case rankings for: read-heavy API
cache, high-concurrency/stampede, microservice-with-TTL, low-memory/IoT/embedded, simple
in-process, distributed-ready, resilience/fail-safe, reactive/real-time, and
**GC-sensitive / large working set** (`Reporting/Scoring.cs`).

## Results (reference run)

> `.NET 10` · Intel i7-10750H (12 logical cores) · `standard` profile
> (concurrency 12, stampede 256, memory 100 000 entries).
> Absolute numbers vary per machine; the **relative story is stable**.

### 🏆 Overall score

Normalized 0–1 per metric (1 = best of four), then weighted (`Reporting/Scoring.cs`).

| Rank | Cache | Hit | Throughput | Miss | Alloc | Memory | Stampede | GC pause | **Overall** |
|:----:|-------|:---:|:----------:|:----:|:-----:|:------:|:--------:|:--------:|:-----------:|
| 🥇 1 | **ActualLab.Fusion** | 1.000 | 1.000 | 0.947 | 1.000 | 0.800 | 1.000 | 1.000 | **0.975** |
| 🥈 2 | **MemoryCache** | 0.815 | 0.910 | 1.000 | 0.857 | 0.764 | 0.000 | 0.826 | **0.744** |
| 🥉 3 | **LiteAPI.Cache** | 0.762 | 0.000 | 0.385 | 0.000 | 1.000 | 1.000 | 1.000 | **0.617** |
| 4 | **FusionCache** | 0.000 | 0.871 | 0.000 | 0.836 | 0.000 | 1.000 | 0.000 | **0.370** |

### ⚡ Latency & throughput

| Cache | Hit (get) | Miss (compute) | Set | Concurrent hit | Mixed 90/10 | Hit alloc/op |
|-------|----------:|---------------:|----:|---------------:|------------:|-------------:|
| MemoryCache | 470 ns | **1.34 µs** | **308 ns** | 15.8 M/s | **13.0 M/s** | 168 B |
| FusionCache | 985 ns | 2.22 µs | 335 ns | 15.3 M/s | 7.3 M/s | 184 B |
| ActualLab.Fusion | **353 ns** | 1.39 µs | 2.52 µs | **16.9 M/s** | 2.5 M/s | **60 B** |
| LiteAPI.Cache | 504 ns | 1.88 µs | 893 ns | 4.6 M/s | 4.6 M/s | 816 B |

### 🛡️ Cache stampede — 256 concurrent GETs of one cold key

Factory calls = how many times the expensive work actually ran (1 = fully protected). Every
cache with a native single-flight get-or-compute collapses it to **one**.

| Cache | Factory calls | Verdict |
|-------|--------------:|---------|
| MemoryCache | 256 | ❌ no protection |
| **FusionCache** | **1** | 🛡️ full protection |
| **ActualLab.Fusion** | **1** | 🛡️ full protection |
| **LiteAPI.Cache** | **1** | 🛡️ full protection |

### 🧠 Memory footprint — 100 000 entries

LiteAPI.Cache stores values **off-heap**, so its managed bytes/entry is ~0 by design.

| Cache | Managed total | Managed / entry | Working-set delta |
|-------|--------------:|----------------:|------------------:|
| MemoryCache | 15.7 MB | 165 B | 25.0 MB |
| FusionCache | 66.6 MB | 698 B | 60.2 MB |
| ActualLab.Fusion | 13.3 MB | 140 B | 82.9 MB |
| **LiteAPI.Cache** | **3.0 KB** | **0 B** | 45.4 MB |

### ⏸️ GC pause + retention — holding 300 000 entries

Marginal wall time a forced full **blocking** GC adds because the cache holds the set, and
how much of the set survives the GC. A near-zero pause **with low retention** means the cache
dodged the pause by *evicting* its entries — it isn't really holding the working set.

| Cache | Managed added | Marginal GC pause | Retained after GC |
|-------|--------------:|------------------:|:-----------------:|
| MemoryCache | 48.7 MB | 26.3 ms | 100% |
| FusionCache | 202.6 MB | 150.9 ms | 100% |
| ActualLab.Fusion | 0 B | ~0 ms | **0% ⚠️ evicts** |
| **LiteAPI.Cache** | **512 B** | **~0 ms** | **100%** |

### 🎯 Best cache per use case

| Use case | Winner | Runner-up |
|----------|--------|-----------|
| Read-heavy API cache | **ActualLab.Fusion** | MemoryCache |
| High-concurrency / stampede | **ActualLab.Fusion** | FusionCache |
| Microservice w/ TTL | **MemoryCache** | FusionCache |
| Low-memory / IoT / embedded | **ActualLab.Fusion** | MemoryCache |
| Simple in-process cache | **ActualLab.Fusion** | MemoryCache |
| Distributed-ready | **ActualLab.Fusion** | FusionCache |
| Resilience / fail-safe | **FusionCache** | LiteAPI.Cache |
| Reactive / real-time | **ActualLab.Fusion** | FusionCache |
| **GC-sensitive / large working set** | **LiteAPI.Cache** | MemoryCache |

## Conclusion

- **Stampede protection is the sharpest divider — and a three-way tie.** Under 256 concurrent
  misses of one key, **FusionCache**, **ActualLab.Fusion** and **LiteAPI.Cache** all run the
  factory **once**; only **MemoryCache** runs it 256 times. Protection only works when the cache
  owns the factory call, so all three are measured through their native get-or-compute (a
  hand-rolled `TryGet → factory → Set` would defeat it). If your cache fronts an expensive
  DB/HTTP call under load, this single property matters more than nanoseconds.
- **ActualLab.Fusion tops the overall score** — fastest reads (~350 ns, 60 B/op), stampede dedup,
  auto-invalidation. But it is a *reactive computed-services* framework, not a TTL key/value
  store: its `Set` is slow (invalidate-then-recompute), and its computed results are held by
  **weak references**, so under memory pressure the GC quietly evicts them (see below) — great
  for recompute-cheap reactive graphs, wrong for "must stay cached".
- **MemoryCache is the pragmatic default** — in-box, TTL, top-tier raw speed (best set / mixed /
  miss here), low allocations. Its one gap is stampede protection; add a lock or reach for one of
  the other three if that bites.
- **FusionCache is the resilience choice** — it trades a few hundred nanoseconds for stampede +
  fail-safe + soft/hard timeouts + a distributed backplane, and **wins the resilience and
  distributed use-cases**. Judge it by features, not the raw-speed score.
- **LiteAPI.Cache wins GC-sensitive / large working sets — measured, not asserted.** Holding
  300 000 entries, a full GC pauses **~0 ms** for LiteAPI vs 26 ms (MemoryCache) and 151 ms
  (FusionCache), because its values live **off-heap** (512 B managed added). Crucially it still
  **retains 100%** of the set — unlike ActualLab.Fusion, which also shows ~0 ms pause but only
  because the GC **evicted everything** (0% retained). The trade-off: LiteAPI's string-returning
  reads decode a managed `string` per get (816 B/op), so its per-op throughput trails the
  reference-storing caches. Its niche is exactly what its name-drop suggests: **huge, long-lived
  working sets where GC pause is the enemy** (game servers, HFT, large hot datasets). (A
  zero-alloc `GetOrCompute(byte[] key, Span<byte> dest, …)` exists but needs a byte-oriented
  adapter surface, outside this common string API.)

**TL;DR:** default → **MemoryCache**; fastest protected reads → **ActualLab.Fusion**;
resilience / distributed → **FusionCache**; huge working set where GC pause hurts →
**LiteAPI.Cache**.

## Design notes & caveats

- **Common surface, native path for each.** Every cache is measured through *its own*
  get-or-compute so stampede protection is exercised fairly: MemoryCache `GetOrCreateAsync`,
  FusionCache `GetOrSetAsync`, LiteAPI.Cache `GetOrComputeAsync` (single-flight — the library
  runs the factory, so it can dedupe concurrent misses), and for ActualLab.Fusion the factory
  *is* the `[ComputeMethod]` body (a "set" is invalidate-then-recompute). A hand-rolled
  `TryGet → factory → Set` would bypass the library's dedup and is deliberately avoided; the
  factory delegate is hoisted to a field so miss numbers reflect the cache, not per-call
  closure garbage.
- **GC pause is measured as a delta, with a retention check.** We time a forced full blocking
  GC with the cache empty, then again holding N entries; the difference isolates the cache's own
  marking cost (the shared key array and unrelated process state cancel). We then re-read the
  whole set to see how much survived — a cache that shows ~0 ms pause but low retention only
  dodged the pause by evicting (ActualLab.Fusion's weak-referenced computeds do exactly this at
  0% retained). The `GC-sensitive / large working set` use-case therefore weights retention, so
  "no pause because I dropped your data" doesn't win it. `SetMaxItems` is set well above every
  scenario's working set so item-count eviction never confounds the measurement.
- **Fusion needs a source generator** — `ActualLab.Generators` is referenced so the compute
  proxy is generated at build time.
- **Determinism.** A fixed-seed factory produces identical fixed-size values; keys are
  generated deterministically. The factory counts its own invocations to measure stampede
  protection exactly.
- **Memory metric.** The cached value is a shared fixed-size payload, so *managed bytes/entry*
  mostly reflects each cache's **per-entry wrapper overhead** (MemoryCache entry vs Fusion
  `Computed<T>` vs off-heap slot), not value size. LiteAPI's ~0 managed bytes is its off-heap
  design; its real memory shows in the working-set delta.
- **Scores are a heuristic.** Overall score weights raw speed/memory; the **use-case rankings**
  re-weight per scenario and fold in declared features — that's where FusionCache's resilience
  and Fusion's reactivity surface. Read the ranking that matches *your* use case, not just the
  overall number.

## Requirements

- .NET SDK **10.0+**
- All dependencies restore from NuGet; no external services needed.
