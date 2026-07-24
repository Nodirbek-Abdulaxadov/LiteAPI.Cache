namespace CacheBench.Caches;

// Qualitative, design-level traits of each cache (not measured — declared).
public sealed record CacheFeatures(
    bool StampedeProtection,     // dedupes concurrent misses for the same key
    bool Ttl,                    // absolute/relative expiration
    bool FailSafe,               // can serve stale on factory failure
    bool DistributedBackplane,   // multi-node coherence (Redis, RPC, …)
    bool AutoInvalidation,       // reactive dependency invalidation
    bool AsyncNative,            // first-class async factory
    bool ZeroExternalDeps);      // no heavy dependency graph

// Common surface every cache is measured through: get-or-compute (the core
// caching operation), set, and reset (clear between scenarios).
public interface ICacheAdapter : IAsyncDisposable
{
    string Name { get; }
    CacheFeatures Features { get; }

    ValueTask<string> GetOrComputeAsync(string key);
    ValueTask SetAsync(string key, string value);
    void Reset();
}
