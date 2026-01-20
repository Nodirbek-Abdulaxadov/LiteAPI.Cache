# LiteAPI.Cache (JustCache)

Fast, GC-free, cross-platform in-memory cache for .NET backed by Rust.

## Install

```bash
dotnet add package LiteAPI.Cache
```

> Requires a precompiled native Rust library. Build RustLib or use packaged runtimes.

## Quick start

```csharp
using LiteAPI.Cache;

JustCache.Initialize();

JustCache.SetString("hello", "world");
Console.WriteLine(JustCache.GetString("hello"));

JustCache.SetStringWithTtl("temp", "value", TimeSpan.FromSeconds(1));
Console.WriteLine(JustCache.TtlMs("temp"));

JustCache.Remove("hello");
JustCache.ClearAll();
```

## Key features

- Native Rust-backed, in-memory cache with a GC-free hot path.
- Cross-platform support (Windows, Linux, macOS) with runtime RID loading.
- Byte[] and string APIs for cache keys and values.
- TTL support with millisecond precision and Redis-style TTL semantics.
- LRU sizing controls and item count metrics.
- Append-only file (AOF) persistence load/enable/disable.
- Redis-like data structures: hashes, lists, sets, sorted sets, and streams.
- Pub/Sub channels and keyspace notifications for expired/evicted keys.
- JSON path get/set for stored JSON values.
- Secondary numeric index and key search via `FindKeys`.
- Optional JSON serialization helpers for objects and collections.

## Usage

- Initialize the native layer once: `JustCache.Initialize()`.
- Set/get bytes or strings: `Set`, `Get`, `SetString`, `GetString`.
- TTL operations: `SetWithTtl`, `SetStringWithTtl`, `Expire`, `TtlMs`.
- LRU sizing: `SetMaxItems`, `GetMaxItems`, `Count`.
- Binary keys: `Set(byte[] key, byte[] value)`, `Get(byte[] key)`, `Remove(byte[] key)`.
- Hashes: `HSet`, `HGet`, `HGetAll`.
- Lists: `LPush`, `RPop`, `LRange`.
- Sets: `SAdd`, `SIsMember`.
- Sorted sets: `ZAdd`, `ZRange`.
- Streams: `XAdd`, `XRange`.
- Pub/Sub: `Subscribe`, `Publish`, `TryPoll`, `Unsubscribe`.
- Notifications: `TryPollNotification`, `ClearNotifications`.
- JSON path: `JsonGet`, `JsonSet`.
- Search: `CreateNumericIndex`, `FindKeys`.
- Scripting: `Eval`, `EvalString`.
- Object helpers: `SetObject`, `GetObject`, `SetObjects`, `GetObjects`.

## Notes

- Native library is required at runtime.
- Full documentation will live in a separate docs project.

## License

MIT License © 2025 LiteAPI