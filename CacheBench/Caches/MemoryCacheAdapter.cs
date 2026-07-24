using CacheBench.Workload;
using Microsoft.Extensions.Caching.Memory;

namespace CacheBench.Caches;

// Microsoft.Extensions.Caching.Memory — the .NET in-box cache. GetOrCreateAsync
// has NO stampede protection: concurrent misses on the same key each run the
// factory.
public sealed class MemoryCacheAdapter : ICacheAdapter
{
    private readonly Factory _factory;
    private readonly TimeSpan _ttl;
    private MemoryCache _cache;

    public MemoryCacheAdapter(Factory factory, TimeSpan ttl)
    {
        _factory = factory;
        _ttl = ttl;
        _cache = New();
    }

    private static MemoryCache New() => new(new MemoryCacheOptions());

    public string Name => "MemoryCache";

    public CacheFeatures Features => new(
        StampedeProtection: false, Ttl: true, FailSafe: false,
        DistributedBackplane: false, AutoInvalidation: false,
        AsyncNative: true, ZeroExternalDeps: true);

    public async ValueTask<string> GetOrComputeAsync(string key)
    {
        return (await _cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _ttl;
            return await _factory.ComputeAsync(key);
        }))!;
    }

    public ValueTask SetAsync(string key, string value)
    {
        _cache.Set(key, value, _ttl);
        return default;
    }

    public void Reset()
    {
        _cache.Dispose();
        _cache = New();
    }

    public ValueTask DisposeAsync()
    {
        _cache.Dispose();
        return default;
    }
}
