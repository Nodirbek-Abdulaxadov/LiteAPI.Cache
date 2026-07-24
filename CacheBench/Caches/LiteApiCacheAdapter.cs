using System.Text;
using CacheBench.Workload;
using LiteAPI.Cache;

namespace CacheBench.Caches;

// LiteAPI.Cache (JustCache) — a low-level, Redis-like, off-heap string/byte
// cache. It exposes a native single-flight GetOrComputeAsync (since 2.3.0;
// returns ValueTask since 2.4.0) that runs the factory itself, so concurrent
// misses on the same key are deduplicated (stampede protection). We call that
// API — the same apples-to-apples path FusionCache and ActualLab.Fusion are
// measured through. (2.5.0/2.6.0 further cut read latency and made the native
// read path read-lock-scalable; see the repo's CHANGELOG.)
public sealed class LiteApiCacheAdapter : ICacheAdapter
{
    private readonly Factory _factory;
    private readonly TimeSpan _ttl;
    private readonly Func<string, CancellationToken, Task<byte[]>> _nativeFactory;
    private static int _initialized;

    public LiteApiCacheAdapter(Factory factory, TimeSpan ttl)
    {
        _factory = factory;
        _ttl = ttl;
        // Hoisted once so the get-or-compute call site allocates no per-call closure.
        _nativeFactory = async (k, ct) => Encoding.UTF8.GetBytes(await _factory.ComputeAsync(k));
        EnsureInitialized();
    }

    private static void EnsureInitialized()
    {
        // JustCache is a process-global singleton; initialize exactly once.
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            JustCache.Initialize();
            JustCache.SetMaxItems(1_500_000); // capacity well above every scenario's working set
        }
    }

    public string Name => "LiteAPI.Cache";

    public CacheFeatures Features => new(
        StampedeProtection: true, Ttl: true, FailSafe: false,
        DistributedBackplane: false, AutoInvalidation: false,
        AsyncNative: true, ZeroExternalDeps: true);

    public async ValueTask<string> GetOrComputeAsync(string key)
    {
        // Native get-or-compute: the library runs the factory, so concurrent
        // misses for the same key collapse into a single factory call.
        byte[] bytes = await JustCache.GetOrComputeAsync(key, _nativeFactory, _ttl, CancellationToken.None);
        return Encoding.UTF8.GetString(bytes);
    }

    public ValueTask SetAsync(string key, string value)
    {
        JustCache.SetStringWithTtl(key, value, _ttl);
        return default;
    }

    public void Reset() => JustCache.ClearAll();

    public ValueTask DisposeAsync() => default;
}
