using ActualLab.Fusion;
using CacheBench.Workload;
using Microsoft.Extensions.DependencyInjection;

namespace CacheBench.Caches;

// ActualLab.Fusion — a reactive "computed services" framework. Its cache is a
// [ComputeMethod]: results are memoized per argument and deduplicate concurrent
// computations. It is not a TTL key/value store; it caches until invalidated.
public class FusionComputeService : IComputeService
{
    private readonly Factory _factory;
    public FusionComputeService(Factory factory) => _factory = factory;

    [ComputeMethod]
    public virtual async Task<string> GetAsync(string key)
        => await _factory.ComputeAsync(key);
}

public sealed class FusionComputeAdapter : ICacheAdapter
{
    private readonly Factory _factory;
    private ServiceProvider _provider;
    private FusionComputeService _service;

    public FusionComputeAdapter(Factory factory)
    {
        _factory = factory;
        (_provider, _service) = Build(factory);
    }

    private static (ServiceProvider, FusionComputeService) Build(Factory factory)
    {
        var services = new ServiceCollection();
        services.AddSingleton(factory);
        services.AddFusion().AddService<FusionComputeService>();
        var provider = services.BuildServiceProvider();
        return (provider, provider.GetRequiredService<FusionComputeService>());
    }

    public string Name => "ActualLab.Fusion";

    public CacheFeatures Features => new(
        StampedeProtection: true, Ttl: false, FailSafe: false,
        DistributedBackplane: true, AutoInvalidation: true,
        AsyncNative: true, ZeroExternalDeps: false);

    public async ValueTask<string> GetOrComputeAsync(string key)
        => await _service.GetAsync(key);

    // Fusion has no direct "set"; a write is modeled as invalidate-then-recompute.
    public async ValueTask SetAsync(string key, string value)
    {
        using (Invalidation.Begin())
            _ = _service.GetAsync(key);
        await _service.GetAsync(key);
    }

    public void Reset()
    {
        _provider.Dispose();
        (_provider, _service) = Build(_factory);
    }

    public ValueTask DisposeAsync()
    {
        _provider.Dispose();
        return default;
    }
}
