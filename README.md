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

JustCache.Remove("hello");
JustCache.ClearAll();
```

## Key features

- Native Rust-backed in-memory cache with a GC-free hot path.
- 16-way internal sharding — concurrent operations on different keys
  do not block each other.
- Two read primitives: `TryGet` (promotes LRU recency, write lock) and
  `TryPeek` (no LRU promotion, read lock — scales for read-heavy
  workloads).
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

## Reads: TryGet vs TryPeek

The Rust core uses `LruCache`, which mutates its recency list on every
`get` — so a read in the LRU sense needs the shard's write lock and
serializes with other writes on that shard. Two API shapes:

| API       | Lock        | LRU promotion on read | When to use                                              |
|-----------|-------------|------------------------|----------------------------------------------------------|
| `TryGet`  | shard write | yes                    | eviction targets least-recently-used                     |
| `TryPeek` | shard read  | no                     | reads scale with concurrency; LRU updates only on writes |

Pick `TryPeek` when:
- Your working set comfortably fits the cache (evictions are rare).
- You're handling a lot of concurrent reads and per-shard write-lock
  serialization is the bottleneck.
- LRU recency on read is not part of your eviction strategy.

Pick `TryGet` when you want classic LRU semantics where every read
promotes the entry away from the eviction candidates.

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

Interpretation:
- `Set` scales ~2.6× from 1 → 16 threads. The 16 shard write locks let
  unrelated keys mutate the cache in parallel.
- `TryPeek` beats `TryGet` at every concurrency level by ~10%; the
  read lock removes per-shard serialization.
- Past ~4 threads the curves flatten — at this point the bottleneck is
  the FFI floor (~150 ns/op), not lock contention.

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
