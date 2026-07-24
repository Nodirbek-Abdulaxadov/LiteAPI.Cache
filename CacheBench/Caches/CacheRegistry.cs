using CacheBench.Workload;

namespace CacheBench.Caches;

public static class CacheRegistry
{
    // Canonical order used everywhere in reporting.
    public static readonly string[] Names =
        { "MemoryCache", "FusionCache", "ActualLab.Fusion", "LiteAPI.Cache" };

    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    // Fresh adapter instances bound to the given factory (so factory-call counts
    // reset with the caller's Factory).
    public static List<ICacheAdapter> CreateAll(Factory factory, TimeSpan? ttl = null)
    {
        var t = ttl ?? DefaultTtl;
        return new List<ICacheAdapter>
        {
            new MemoryCacheAdapter(factory, t),
            new FusionCacheAdapter(factory, t),
            new FusionComputeAdapter(factory),
            new LiteApiCacheAdapter(factory, t),
        };
    }

    // One fresh adapter bound to a specific factory (isolated call counter).
    public static ICacheAdapter Create(string name, Factory factory, TimeSpan? ttl = null)
    {
        var t = ttl ?? DefaultTtl;
        return name switch
        {
            "MemoryCache" => new MemoryCacheAdapter(factory, t),
            "FusionCache" => new FusionCacheAdapter(factory, t),
            "ActualLab.Fusion" => new FusionComputeAdapter(factory),
            "LiteAPI.Cache" => new LiteApiCacheAdapter(factory, t),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    public static CacheFeatures FeaturesOf(string name) => name switch
    {
        "MemoryCache" => new MemoryCacheAdapter(new Factory(), DefaultTtl).Features,
        "FusionCache" => new FusionCacheAdapter(new Factory(), DefaultTtl).Features,
        "ActualLab.Fusion" => new FusionComputeAdapter(new Factory()).Features,
        "LiteAPI.Cache" => new LiteApiCacheAdapter(new Factory(), DefaultTtl).Features,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };
}
