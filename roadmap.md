# LiteAPI.Cache (JustCache) Roadmap

This roadmap outlines the evolution of **LiteAPI.Cache** from a simple key-value store to a feature-rich, Redis-like in-memory data engine. The goal is to bring "Core Redis" and "Redis Stack" capabilities to your embedded .NET solution.

## 📍 Current Status
- **Core**: In-memory storage, String/Binary data types.
- **TTL**: Per-key expiration (lazy checks + manual pruning).
- **Persistence**: Manual JSON Snapshot/Restore (like Redis RDB).
- **Architecture**: Native Rust backend with .NET P/Invoke wrapper.
- **Concurrency**: Thread-safe global lock (`RwLock`).

---

## 🚀 Phase 1: Rich Data Structures (Core Redis)
Move beyond simple keys to structured data types. This requires refactoring the Rust `Entry` struct to hold an enum of types.

- [ ] **Hashes (`HSET`, `HGET`, `HGETALL`)**: Store maps within keys. Useful for object caching without full serialization overhead.
- [ ] **Lists (`LPUSH`, `RPOP`, `LRANGE`)**: Linked lists for queues or recent history.
- [ ] **Sets (`SADD`, `SISMEMBER`)**: Unique collections for tagging and deduplication.
- [ ] **Sorted Sets (`ZADD`, `ZRANGE`)**: Sets with scoring, critical for leaderboards and priority queues.
- [ ] **C# API Expansion**: Add `JustCache.Hashes`, `JustCache.Lists` namespaces to `LiteAPI.Cache`.

## 🛡️ Phase 2: Reliability & Advanced Persistence
Improve consistency and data safety.

- [ ] **LRU Eviction**: Replace the current random eviction with "Least Recently Used" to keep hot data (use `lru` crate in Rust).
- [ ] **Auto-Expiry**: Implement a background thread in Rust to proactively remove expired keys (simulating Redis active expiry).
- [ ] **AOF (Append Only File)**: Implement a write-ahead log to support crash recovery without full snapshots.
- [ ] **Binary-Safe Keys**: Allow `byte[]` keys in addition to strings, matching Redis flexibility.

## ⚡ Phase 3: High Availability & Messaging
Add capabilities for real-time applications.

- [ ] **Pub/Sub**: generic Publish/Subscribe mechanism for in-process event buses (e.g., `JustCache.Subscribe("channel")`).
- [ ] **Keyspace Notifications**: Trigger C# events when keys expire or are evicted (`OnKeyExpired`, `OnKeyEvicted`).
- [ ] **Streams**: Append-only log data structure for event sourcing patterns.

## 🧠 Phase 4: Programmability & Search (Redis Stack)
Advanced features for complex data needs.

- [ ] **JSON Path Support**: Allow modifying fields inside a JSON value without full re-serialization (via `serde_json` path querying).
- [ ] **Secondary Indexing (Search)**: Create indices on values to allow `Find("age > 20")` queries.
- [ ] **Scripting**: potential for embedding Lua or a lightweight script engine for atomic complex operations.

## 🔮 Future / Exploration
- **Server Mode**: Wrap the library in a lightweight TCP server implementing the **RESP (Redis Serialization Protocol)**. This would allow `LiteAPI.Cache` to act as a drop-in Redis replacement for standard Redis clients.

---

## 🛠️ Immediate Next Steps (Action Items)
1.  **Refactor**: Modify `RustLib` to use an `enum Value { Bytes(Vec<u8>), Hash(HashMap<String, Vec<u8>>), ... }`.
2.  **Integrate**: Update `LiteAPI.Cache` C# marshaling to handle method calls for these new types.
