# LiteAPI.Cache - JustCache

**GC-free, cross-platform in-memory cache for .NET backed by Rust.**

JustCache is a high-performance memory cache system built to bypass .NET's garbage collector by leveraging native Rust memory management. Designed for low-latency, high-throughput scenarios where predictability and performance are essential.

---

## 🚀 Key Features

- ⚡ **GC-Free**: No garbage collection pressure in .NET
- 🧠 **Native performance** using Rust under the hood
- 💼 **Cross-platform**: Supports Windows, Linux, and macOS
- 🔒 **Thread-safe** read/write access
- 💾 **Supports** strings, byte arrays, and JSON-serializable objects
- 🧱 **Phase 1 (Core Redis types)**: Hashes, Lists, Sets, Sorted Sets
- 🛡️ **Phase 2 (Reliability)**: LRU eviction, TTL + active expiry thread, AOF replay, binary-safe keys
- 🧩 **Interop via NativeAOT or P/Invoke**
- 🛡️ **Safe memory management** without leaks

---

## 📦 Installation

Install the NuGet package:

```bash
dotnet add package LiteAPI.Cache
```


> 🔧 Requires a precompiled native Rust dynamic library. See the documentation or [GitHub repository](https://github.com/Nodirbek-Abdulaxadov/LiteAPI.Cache) for details.

---

## ⚙️ Usage

- **Initialize the cache** at application startup
- **Set/Get** data by key (supports string, bytes, and object types)
- **Remove** individual keys or **clear all**
- **Interop with Rust** is handled internally—no manual marshaling needed

```csharp
using LiteAPI.Cache;

string key = "example_key";
Student student = Student.Random(1);

// Initialize the cache and perform operations
JustCache.Initialize();

// Set an object in the cache
JustCache.SetObject(key, student);

// Retrieve the object from the cache
student = JustCache.GetObject<Student>(key) ?? Student.Random(2);

// Display the retrieved object
Console.WriteLine(student);

// Remove the object from the cache
JustCache.Remove(key);

// Clear all cached objects
JustCache.ClearAll();
```

---

## 🧱 Phase 1: Rich Data Structures (Core Redis)

Phase 1 adds Redis-like data structures implemented in Rust and exposed via the C# `JustCache` API.

### Hashes (`HSET`, `HGET`, `HGETALL`)

```csharp
JustCache.HSetString("user:1", "name", "Alice");
JustCache.HSetString("user:1", "city", "Tashkent");

string? name = JustCache.HGetString("user:1", "name");
var all = JustCache.HGetAll("user:1"); // Dictionary<string, byte[]>
```

### Lists (`LPUSH`, `RPOP`, `LRANGE`)

```csharp
JustCache.LPushString("recent", "a");
JustCache.LPushString("recent", "b");

List<string> items = JustCache.LRangeStrings("recent", 0, -1);
string? last = JustCache.RPopString("recent");
```

### Sets (`SADD`, `SISMEMBER`)

```csharp
bool added = JustCache.SAddString("tags", "x");
bool isMember = JustCache.SIsMemberString("tags", "x");
```

### Sorted Sets (`ZADD`, `ZRANGE`)

```csharp
JustCache.ZAdd("leaderboard", 5, "alice");
JustCache.ZAdd("leaderboard", 10, "bob");

List<string> members = JustCache.ZRange("leaderboard", 0, -1);
```

---

## 🛡️ Phase 2: Reliability & Advanced Persistence

Phase 2 focuses on predictable memory usage, better expiry behavior, and crash recovery.

### LRU eviction

Configure a maximum number of items; least-recently-used entries are evicted when capacity is exceeded.

```csharp
JustCache.SetMaxItems(100_000);
int current = JustCache.Count;
```

### TTL + active expiry

Keys can be written with TTL or updated with expiry. The Rust side runs a small background thread to proactively remove expired entries.

```csharp
JustCache.SetStringWithTtl("session:1", "value", TimeSpan.FromSeconds(10));
bool ok = JustCache.Expire("session:1", TimeSpan.FromSeconds(5));

// Redis-like TTL semantics:
// -2: key does not exist
// -1: no expiry
// >=0: milliseconds remaining
long ttlMs = JustCache.TtlMs("session:1");
```

### AOF (Append Only File) replay

Enable AOF logging to a file, then replay it to rebuild state after a crash/restart.

```csharp
JustCache.EnableAof("./justcache.aof");
JustCache.SetString("aof:k1", "1");
JustCache.DisableAof();

JustCache.ClearAll();
JustCache.LoadAof("./justcache.aof");
```

### Binary-safe keys

Use `byte[]` keys (in addition to string keys), similar to Redis.

```csharp
byte[] key = new byte[] { 0, 1, 2, 255 };
JustCache.Set(key, System.Text.Encoding.UTF8.GetBytes("bin"));
byte[]? val = JustCache.Get(key);
JustCache.Remove(key);
```

---

## ✅ Verifying Phase 1 / Phase 2 (C#)

The repository includes small runners in `TestApp` to validate Rust + P/Invoke interop.

1) Build the Rust native library:

```bash
cd RustLib
cargo build --release
```

2) Build the .NET solution (copies the native artifact into outputs):

```bash
dotnet build -c Release
```

3) Run verification:

```bash
cd TestApp/bin/Release/net9.0
./TestApp.exe phase1
./TestApp.exe phase2
```

---

## 🧠 Why JustCache?

- 🚀 Ultra-fast native cache access
- ✅ No impact on .NET GC or memory fragmentation
- 🧩 Drop-in utility for microservices, real-time systems, or edge apps
- 🔍 Useful for caching config, lookup tables, auth sessions, and more

---

## 🪪 License

MIT License © 2025 LiteAPI

---

## 💬 Feedback

Found a bug or want a feature? Open an issue or PR on [GitHub](https://github.com/Nodirbek-Abdulaxadov/LiteAPI.Cache).


## 🛠️ Contributing

We welcome contributions! Please see the [CONTRIBUTING.md](https://github.com/Nodirbek-Abdulaxadov/LiteAPI.Cache/CONTRIBUTING.md) for guidelines.