# In-Memory Cache Benchmark

- **Generated:** 2026-07-24 07:12:33Z
- **Runtime:** .NET 10.0.10
- **OS:** Microsoft Windows 10.0.22631
- **CPU:** Intel64 Family 6 Model 165 Stepping 3, GenuineIntel (12 logical cores)
- **Profile:** standard · concurrency 12 · stampede 256 · memory 100,000 entries
- **Caches:** MemoryCache, FusionCache (ZiggyCreatures), ActualLab.Fusion, LiteAPI.Cache (JustCache)

> Warmup: 100 iterations per benchmark. All caches measured through a common get-or-compute surface.
> ActualLab.Fusion is a reactive *computed-services* framework (memoization + auto-invalidation), not a TTL store — compared here on the same get-or-compute path.

## Overall Score

Normalized 0–1 (1 = best of four). Overall = weighted blend.

| Rank | Cache | Hit | Throughput | Miss | Alloc | Memory | Stampede | GC pause | **Overall** |
|-----:|-------|----:|-----------:|-----:|------:|-------:|---------:|---------:|------------:|
| 1 | ActualLab.Fusion | 1.000 | 1.000 | 1.000 | 1.000 | 0.795 | 1.000 | 0.951 | **0.970** |
| 2 | MemoryCache | 0.847 | 0.506 | 0.484 | 0.856 | 0.764 | 0.000 | 0.826 | **0.631** |
| 3 | LiteAPI.Cache | 0.732 | 0.000 | 0.579 | 0.000 | 1.000 | 1.000 | 1.000 | **0.628** |
| 4 | FusionCache | 0.000 | 0.580 | 0.000 | 0.835 | 0.000 | 1.000 | 0.000 | **0.318** |

## Feature Matrix

| Cache | Stampede protection | TTL | Fail-safe | Distributed backplane | Auto-invalidation | Async-native | Zero heavy deps |
|-------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| MemoryCache | — | ✅ | — | — | — | ✅ | ✅ |
| FusionCache | ✅ | ✅ | ✅ | ✅ | — | ✅ | ✅ |
| ActualLab.Fusion | ✅ | — | — | ✅ | ✅ | ✅ | — |
| LiteAPI.Cache | ✅ | ✅ | — | — | — | ✅ | ✅ |

## Latency & Throughput

| Cache | Hit (get) | Miss (compute) | Set | Concurrent hit | Mixed 90/10 | Hit alloc/op |
|-------|----------:|---------------:|----:|---------------:|------------:|-------------:|
| MemoryCache | 524 ns | 2.03 µs | 511 ns | 9.97 M/s | 6.72 M/s | 168 B |
| FusionCache | 1.07 µs | 2.58 µs | 460 ns | 10.76 M/s | 7.14 M/s | 184 B |
| ActualLab.Fusion | 426 ns | 1.44 µs | 2.84 µs | 15.19 M/s | 2.42 M/s | 59 B |
| LiteAPI.Cache | 599 ns | 1.92 µs | 927 ns | 4.62 M/s | 4.56 M/s | 816 B |

## Cache Stampede Protection

256 concurrent GETs of one cold key (slow factory). Factory calls = how many times the expensive work actually ran (1 = fully protected).

| Cache | Factory calls | Total time | Verdict |
|-------|--------------:|-----------:|---------|
| MemoryCache | 256 | 30.87 ms | ❌ no protection |
| FusionCache | 1 | 24.61 ms | 🛡️ full protection |
| ActualLab.Fusion | 1 | 20.85 ms | 🛡️ full protection |
| LiteAPI.Cache | 1 | 21.80 ms | 🛡️ full protection |

## Memory Footprint

Managed-heap growth after inserting 100,000 entries. LiteAPI.Cache stores values off-heap (GC-free), so its managed/entry is near zero by design.

| Cache | Managed total | Managed / entry | Working-set delta |
|-------|--------------:|----------------:|------------------:|
| MemoryCache | 15.7 MB | 165 B | 25.2 MB |
| FusionCache | 66.6 MB | 698 B | 60.3 MB |
| ActualLab.Fusion | 13.6 MB | 143 B | 87.0 MB |
| LiteAPI.Cache | 3.0 KB | 0 B | 48.8 MB |

## GC Pause Under a Large Working Set

Forced full **blocking** GC pause measured with the cache empty vs. holding 300,000 long-lived entries. **Marginal** = the pause the cache itself adds (empty and full share the same key array + process state, which cancels). Off-heap caches add almost nothing to the managed graph.

| Cache | Managed added | **Marginal pause (cache adds)** | Retained after GC |
|-------|--------------:|-------------------------------:|------------------:|
| MemoryCache | 48.7 MB | **27.64 ms** | 100% |
| FusionCache | 202.6 MB | **154.82 ms** | 100% |
| ActualLab.Fusion | 11.3 MB | **8.37 ms** | 0% ⚠️ evicts |
| LiteAPI.Cache | 512 B | **0.77 ms** | 100% |

*Retained = fraction of the working set still cached after the GCs. A cache that shows near-zero GC pause **and** low retention dodged the pause by evicting its entries (e.g. weak references) — it isn't actually holding the working set.*

## Use-case Rankings

Weighted blend of measured metrics + declared features. Higher = better fit.

| Use case | 1st | 2nd | 3rd | 4th |
|----------|-----|-----|-----|-----|
| Read-heavy API cache | **ActualLab.Fusion (0.890)** | MemoryCache (0.654) | LiteAPI.Cache (0.499) | FusionCache (0.458) |
| High-concurrency / stampede | **ActualLab.Fusion (1.000)** | FusionCache (0.637) | LiteAPI.Cache (0.481) | MemoryCache (0.396) |
| Microservice w/ TTL | **FusionCache (0.616)** | ActualLab.Fusion (0.600) | MemoryCache (0.569) | LiteAPI.Cache (0.554) |
| Low-memory / IoT / embedded | **ActualLab.Fusion (0.934)** | MemoryCache (0.787) | LiteAPI.Cache (0.660) | FusionCache (0.225) |
| Simple in-process cache | **ActualLab.Fusion (0.969)** | MemoryCache (0.733) | LiteAPI.Cache (0.435) | FusionCache (0.312) |
| Distributed-ready | **ActualLab.Fusion (0.850)** | FusionCache (0.766) | MemoryCache (0.378) | LiteAPI.Cache (0.360) |
| Resilience / fail-safe | **FusionCache (0.858)** | LiteAPI.Cache (0.523) | ActualLab.Fusion (0.450) | MemoryCache (0.335) |
| Reactive / real-time | **ActualLab.Fusion (1.000)** | FusionCache (0.316) | LiteAPI.Cache (0.310) | MemoryCache (0.228) |
| GC-sensitive / large working set | **LiteAPI.Cache (0.937)** | MemoryCache (0.809) | ActualLab.Fusion (0.642) | FusionCache (0.379) |

