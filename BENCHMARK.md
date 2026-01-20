# Benchmarks: Redis vs JustCache vs MemoryCache

Bu fayl LiteAPI.Cache (JustCache) ni `MemoryCache` va local Redis (`localhost:6379`) bilan:
1) performance (BenchmarkDotNet),
2) feature matrix
bo‘yicha solishtirish uchun.

## Tez start

Prerequisites:
- .NET SDK (net9.0)
- Local Redis ishlayotgan bo‘lsin (`localhost:6379`)

Run:
- `dotnet run -c Release --project .\LiteAPI.Cache.Benchmarks\LiteAPI.Cache.Benchmarks.csproj`

Optional env:
- `REDIS_CONNECTION` (default: `localhost:6379`)
- `REDIS_DB` (default: `15`)

## Nima o‘lchanadi?

Benchmark project: [LiteAPI.Cache.Benchmarks/LiteAPI.Cache.Benchmarks.csproj](LiteAPI.Cache.Benchmarks/LiteAPI.Cache.Benchmarks.csproj)

- KV hot-path: `Set`, `Get(hit)`, `Get(miss)`, `Remove`
- TTL: `SetWithTtl` (hammasida), `Expire`, `TTL query` (Redis va JustCache)
- Redis-like structures: Hash/List/Set/SortedSet (Redis va JustCache)

## “GC-free” (acceptance criteria)

Biz bu yerda 2 ta alohida tushunchani ajratamiz:

1) **Cache hot-path GC-free (managed alloc = 0)**
- `Get_Hit`, `Set_Overwrite`, `Remove_Existing` da `Allocated = 0` va `Gen0 = 0` bo‘lishi kerak.
- Buning uchun JustCache’da **caller-buffer** API ishlatiladi: `JustCache.TryGet(byte[] key, Span<byte> destination, out int written)`.
- Shuningdek, hot-path’da `string` marshaling (UTF8 conversion) ham alloc keltirishi mumkin, shuning uchun benchmarklarda key’lar `byte[]` qilib oldindan tayyorlanadi.

2) **Total-system GC-minimal (native/Rust alloc ham minimal)**
- Background jarayonlar (TTL/eviction/AOF) va `Get`/`Set` native tarafdagi yashirin alloc’lar ham minimallashtiriladi.
- Bu alohida workstream: data structure layout, sharding, TTL wheel, arena/slab, va hokazo.

Eslatma: Redis benchmarklari TCP/serialization overhead sabab in-process cache’lardan tabiiy ravishda sekinroq bo‘ladi, lekin distributed, persistence, replication kabi imkoniyatlar beradi.

## Zero-copy “lease” (katta payload uchun)

JustCache’da 2 xil `Get(hit)` yo‘li bor:

- **CopyInto**: `JustCache.TryGet(key, destinationBuffer, out written)`
	- 1 ta native call + `memcpy` (payload hajmiga chiziqli o‘sadi)
	- Hot path managed alloc = 0
- **Lease (zero-copy)**: `using var lease = JustCache.GetLease(key)`
	- 2 ta native call (`get_lease` + `lease_free`), lekin payload’ni ko‘chirmaydi
	- Katta qiymatlarda (masalan 32KB) `memcpy` yo‘qligi sabab juda tezroq

Benchmark: `JustCacheLeaseBenchmarks` (ShortRun)

| Method | Payload | Mean |
|---|---:|---:|
| MemoryCache (Get hit) | 32 B | ~45 ns |
| JustCache CopyInto | 32 B | ~144 ns |
| JustCache Lease | 32 B | ~157 ns |
| Redis (Get hit) | 32 B | ~85 µs |
| MemoryCache (Get hit) | 1 KB | ~43 ns |
| JustCache CopyInto | 1 KB | ~151 ns |
| JustCache Lease | 1 KB | ~156 ns |
| Redis (Get hit) | 1 KB | ~87 µs |
| MemoryCache (Get hit) | 32 KB | ~42 ns |
| JustCache CopyInto | 32 KB | ~1.07 µs |
| JustCache Lease | 32 KB | ~156 ns |
| Redis (Get hit) | 32 KB | ~102 µs |

Xulosa: kichik payload’da `CopyInto` yaxshi (1 call), katta payload’da `Lease` ancha foydali (copy yo‘q).

## Feature matrix (high-level)

| Feature | MemoryCache (Microsoft) | JustCache (LiteAPI.Cache) | Redis |
|---|---:|---:|---:|
| In-process | ✅ | ✅ | ❌ |
| Distributed (multi-node) | ❌ | ❌ | ✅ |
| Network access | ❌ | ❌ | ✅ |
| Persistence | ❌ | ✅ (AOF) | ✅ (AOF/RDB) |
| TTL (absolute) | ✅ | ✅ (ms, Redis-style) | ✅ |
| Sliding expiration | ✅ | ❌ (yo‘q, faqat absolute TTL/Expire) | ❌ (core’da yo‘q) |
| Explicit `Expire(key, ttl)` | ❌ (faqat overwrite via options) | ✅ | ✅ |
| TTL query (`TTL/PTTL`) | ❌ | ✅ (`TtlMs`) | ✅ (`TTL/PTTL`) |
| Eviction policy | ✅ (LRU-ish + priority) | ✅ (LRU max items) | ✅ (config: allkeys-lru/lfu/volatile...) |
| Max size controls | ✅ (SizeLimit) | ✅ (MaxItems) | ✅ (maxmemory + policy) |
| Hash/List/Set/ZSet/Streams | ❌ | ✅ | ✅ |
| Pub/Sub | ❌ | ✅ | ✅ |
| Keyspace notifications | ❌ | ✅ | ✅ (config talab qiladi) |
| Scripting | ❌ | ✅ (`Eval`) | ✅ (Lua) |
| JSON path ops | ❌ | ✅ (`JsonGet/JsonSet`) | ⚠️ (odatda RedisJSON module) |
| Secondary index + key search | ❌ | ✅ (`CreateNumericIndex`, `FindKeys`) | ⚠️ (SCAN + app-side / modules) |
| Transactions | ❌ | ❌ | ✅ (MULTI/EXEC) |
| Replication / Cluster | ❌ | ❌ | ✅ |
| Auth/TLS/ACL | ❌ | ❌ | ✅ |
| Observability | ⚠️ (EventCounters/metrics limited) | ✅ (EventCounters) | ✅ (INFO/slowlog/metrics/exporters) |

### Qisqa tavsiya

- `MemoryCache`: eng tez in-process cache, sliding TTL kerak bo‘lsa eng qulay.
- `JustCache`: in-process bo‘lib, Redis’ga o‘xshash data structure + TTL + LRU + AOF kabi feature’lar bilan, GC-free hot path maqsad qilingan.
- `Redis`: distributed/persistence/replication/cluster kerak bo‘lsa default tanlov; lekin latency (network) har doim bor.
