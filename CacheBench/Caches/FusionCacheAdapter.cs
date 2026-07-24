using CacheBench.Workload;
using ZiggyCreatures.Caching.Fusion;

namespace CacheBench.Caches;

// ZiggyCreatures.FusionCache — batteries-included cache with built-in cache
// stampede protection, fail-safe, soft/hard timeouts and an optional
// distributed backplane.
public sealed class FusionCacheAdapter : ICacheAdapter
{
    private readonly Factory _factory;
    private readonly TimeSpan _ttl;
    private FusionCache _cache;

    public FusionCacheAdapter(Factory factory, TimeSpan ttl)
    {
        _factory = factory;
        _ttl = ttl;
        _cache = New(ttl);
    }

    private static FusionCache New(TimeSpan ttl)
    {
        var options = new FusionCacheOptions();
        options.DefaultEntryOptions.Duration = ttl;
        return new FusionCache(options);
    }

    public string Name => "FusionCache";

    public CacheFeatures Features => new(
        StampedeProtection: true, Ttl: true, FailSafe: true,
        DistributedBackplane: true, AutoInvalidation: false,
        AsyncNative: true, ZeroExternalDeps: true);

    public async ValueTask<string> GetOrComputeAsync(string key)
        => await _cache.GetOrSetAsync<string>(key, async (ctx, ct) => await _factory.ComputeAsync(key));

    public ValueTask SetAsync(string key, string value)
        => _cache.SetAsync(key, value);

    public void Reset()
    {
        _cache.Dispose();
        _cache = New(_ttl);
    }

    public ValueTask DisposeAsync()
    {
        _cache.Dispose();
        return default;
    }
}
