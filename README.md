# LiteAPI.Cache (JustCache)

Fast, GC-free, cross-platform in-memory cache for .NET, backed by a
sharded Rust core.

## Install

```bash
dotnet add package LiteAPI.Cache
```

The NuGet package ships the prebuilt native library for win-x64,
win-arm64, linux-x64, linux-arm64, osx-x64, and osx-arm64 under
`runtimes/<rid>/native/`. No separate install step.

## Quick start

```csharp
using LiteAPI.Cache;

JustCache.Initialize();

JustCache.SetString("hello", "world");
Console.WriteLine(JustCache.GetString("hello"));

JustCache.SetStringWithTtl("temp", "value", TimeSpan.FromSeconds(1));
Console.WriteLine(JustCache.TtlMs("temp"));

// GC-free hot path (no per-call byte[] allocations):
var keyBytes = System.Text.Encoding.UTF8.GetBytes("hot:key");
var buffer = new byte[32 * 1024];
if (JustCache.TryGet(keyBytes, buffer, out var written))
    Console.WriteLine($"bytes={written}");

// Single-flight get-or-compute — concurrent misses of one key run the
// factory exactly once (cache-stampede protection):
byte[] value = JustCache.GetOrCompute(
    "user:42", key => System.Text.Encoding.UTF8.GetBytes("expensive-result"));

JustCache.Remove("hello");
JustCache.ClearAll();
```

## Key features

- Native Rust-backed in-memory cache with a GC-free hot path.
- 16-way internal sharding — concurrent operations on different keys
  do not block each other.
- Single-flight `GetOrCompute` / `GetOrComputeAsync` — the cache runs your
  factory, so concurrent misses of the same key collapse into one call
  (cache-stampede protection).
- Two read primitives, both under the shard read lock: `TryGet`
  (sampled/approximate LRU recency) and `TryPeek` (no recency — maximal
  read scaling).
- TTL with millisecond precision, Redis-style semantics, and a
  timing-wheel reaper (idle caches stay idle).
- LRU eviction with `SetMaxItems` budgeting (approximate per-shard).
- Append-only file (AOF) persistence — write/replay; absolute-expiry
  opcode so TTLs survive restarts correctly.
- Redis-like data structures: hashes, lists (VecDeque-backed; O(1)
  LPUSH), sets, sorted sets, streams.
- Pub/Sub channels and keyspace notifications.
- JSON-path get/set, secondary numeric index + `FindKeys`.
- Cross-platform: Windows / Linux / macOS, x64 and arm64.
- AOT-friendly: source-generated `[LibraryImport]` P/Invokes on
  net7.0+; falls back to classic `[DllImport]` on net6.0.

## Benchmarks

[`CacheBench/`](CacheBench/) is a deterministic comparison of LiteAPI.Cache
against **MemoryCache**, **FusionCache**, and **ActualLab.Fusion** — all
measured through one common get-or-compute surface (each via *its own* native
get-or-compute, so stampede protection is exercised fairly). Run it:

```bash
cd CacheBench
dotnet run -c Release            # tables + charts -> results/
dotnet run -c Release -- verify  # correctness + stampede sanity
```

Headlines from the reference run (full tables and methodology in
[`CacheBench/README.md`](CacheBench/README.md)):

- **Stampede protection: full.** Under 256 concurrent misses of one key,
  LiteAPI.Cache runs the factory **once** — tied with FusionCache and
  ActualLab.Fusion; MemoryCache runs it 256×.
- **GC-sensitive / large working sets: wins.** Holding 300 000 entries, a
  forced full blocking GC adds **~0 ms** (values live off-heap, ~0 managed
  bytes/entry) while still **retaining 100%** of the set — vs 26 ms
  (MemoryCache) and 151 ms (FusionCache).
- **Trade-off:** the common surface returns a managed `string` per read, so
  per-op read throughput trails the reference-storing caches. The
  zero-allocation `TryGet(byte[], Span<byte>)` / `GetOrCompute(byte[],
  Span<byte>, …)` paths avoid that when a byte-oriented API is an option.

## Reads: TryGet vs TryPeek

The Rust core stores values behind an `LruCache`. Since 2.6.0 **both** read
primitives take the shard **read** lock — so concurrent reads of the same
shard no longer serialize — and they differ only in how they track LRU
recency:

| API       | Lock       | LRU recency on read                          | When to use                                          |
|-----------|------------|----------------------------------------------|------------------------------------------------------|
| `TryGet`  | shard read | **sampled** (~1 in 8, under a brief write lock) | LRU-style eviction that still scales for reads    |
| `TryPeek` | shard read | none                                         | maximal read scaling; recency updates only on writes |

Before 2.6.0, `TryGet` took the write lock on every read (exact LRU) and
serialized reads on a shard. It now reads under the read lock and promotes
recency on only a sampled fraction of hits — so it scales like a read while
keeping *approximate* LRU.

Pick `TryPeek` when:
- Your working set comfortably fits the cache (evictions are rare).
- LRU recency on read is not part of your eviction strategy.

Pick `TryGet` when you want LRU-style eviction where reads keep hot entries
away from the eviction candidates — now without the per-read write lock. Its
recency is approximate; if you need recency bumped on **every** read, use the
object-returning `Get` / `GetString`, which still take the write lock.

## Concurrent throughput

`dotnet run --project TestApp -c Release -- concurrent` runs an in-tree
benchmark that hammers the cache from N threads. Sample numbers from a
local 8-core box (your numbers will differ — run it):

```
threads  TryGet ops/s   TryPeek ops/s   Set ops/s
     1     4,181,144      4,580,017     1,646,768
     2     4,908,301      6,641,879     1,899,029
     4     5,884,286      6,563,840     3,271,211
     8     5,867,490      6,459,659     4,352,627
    16     6,119,980      6,361,747     4,302,333
```

Interpretation (the sample table above predates the 2.6.0 read-lock change
to `TryGet`):
- `Set` scales ~2.6× from 1 → 16 threads. The 16 shard write locks let
  unrelated keys mutate the cache in parallel.
- Since 2.6.0 `TryGet` also reads under the shard read lock (sampled LRU),
  so it now tracks close to `TryPeek`; the small remaining gap is the
  occasional recency-promotion write lock.
- Past a few threads the curves flatten. On low-contention (spread) reads
  the ceiling is the per-call managed↔native interop, not the lock — so
  the read-lock win shows up under *contention* (hot keys → hot shards),
  where the write lock used to serialize.

## API surface

### KV (string + binary keys)

| Operation               | Notes                                              |
|-------------------------|----------------------------------------------------|
| `Initialize()`          | Boot the native engine (once).                     |
| `Set / Get`             | String keys, byte[] values. Allocates per call.    |
| `Set(byte[], byte[])`   | Binary keys. Allocation-free key path.             |
| `TryGet(byte[], Span, out int)` | GC-free hot path; copies into caller buffer. |
| `TryPeek(byte[], Span, out int)`| GC-free + shard read lock. See above.       |
| `GetLease(byte[])`      | Zero-copy: returns a `ref struct` wrapping the value pointer. `using` disposes the native handle. |
| `Remove / ClearAll`     |                                                    |

### Get-or-compute (single-flight / stampede protection)

The cache runs your factory, so concurrent misses of the same key collapse
into one call.

| Operation | Notes |
|-----------|-------|
| `GetOrCompute(key, factory)` (+ `ttl`)          | Sync; concurrent misses run the factory once.               |
| `GetOrComputeString(key, factory)` (+ `ttl`)    | String value; a hit decodes straight off the native buffer. |
| `GetOrComputeAsync(key, factory, ct)` (+ `ttl`) | Async; returns `ValueTask<byte[]>` (a hit allocates no `Task`). |
| `GetOrCompute(byte[] key, Span<byte> dst, factory, out written)` | Zero-allocation protected hit — copies into the caller buffer. |

### TTL

| Operation                       | Notes                                  |
|---------------------------------|----------------------------------------|
| `SetWithTtl / SetStringWithTtl` | Single call sets value + TTL.          |
| `Expire(key, ttl)`              | Apply TTL to an existing key.          |
| `TtlMs(key)`                    | `-2` missing, `-1` no TTL, `>=0` ms.   |

### Eviction

| Operation        | Notes                                          |
|------------------|------------------------------------------------|
| `SetMaxItems(n)` | Budget split across 16 shards. With `n < 16` each shard still gets one slot, so total cap is at least 16. |
| `GetMaxItems`    |                                                |
| `Count`          | Sum across all shards.                         |

### Redis-like structures

`HSet / HGet / HGetAll`, `LPush / RPop / LRange`,
`SAdd / SIsMember`, `ZAdd / ZRange`, `XAdd / XRange`.

### Persistence

`AofEnable(path) / AofDisable() / AofLoad(path)`. The on-disk format
is versioned per opcode; v2.2.0+ writes the absolute-expiry opcode
(`AOF_OP_EXPIRE_AT`) so TTLs survive restarts without being
re-anchored to replay time.

### Pub/Sub & notifications

`Subscribe / Unsubscribe / Publish / TryPoll`.
`TryPollNotification / ClearNotifications`.

### Other

`JsonGet / JsonSet`, `CreateNumericIndex / FindKeys`, `Eval / EvalString`,
`SetObject / GetObject / SetObjects / GetObjects`.

## Architecture (Rust side)

```
src/
  lib.rs              # FFI entrypoints, cache state, expiry thread
  aof.rs              # Append-only file write + read primitives
  expiry.rs           # Timing-wheel min-heap
  jsonpath.rs         # Minimal JSONPath parser + get/set
  notifications.rs    # Keyspace notification FIFO
  pubsub.rs           # Pub/Sub state
```

Cache state layout:

```rust
struct ShardedCache {
    shards:  Vec<RwLock<CacheState>>,   // 16 of these
    indexes: RwLock<Indexes>,            // global numeric secondary indexes
}
```

Lock-acquisition order is always **shard → indexes** to prevent
deadlock. Every `#[no_mangle] pub extern "C" fn` is wrapped in
`catch_unwind`, so panics return a sentinel (null / -1) and log to
stderr instead of unwinding into managed code.

## Building from source

```bash
# Native library
cd RustLib
cargo build --release

# .NET (auto-syncs the built .so/.dylib/.dll into the project)
cd ..
dotnet build LiteAPI.Cache.sln -c Release

# Smoke + integration tests
dotnet run --project TestApp -c Release -- phase1
dotnet test LiteAPI.Cache.IntegrationTests -c Release

# Concurrent benchmark
dotnet run --project TestApp -c Release -- concurrent
```

## License

MIT — © 2025 LiteAPI
