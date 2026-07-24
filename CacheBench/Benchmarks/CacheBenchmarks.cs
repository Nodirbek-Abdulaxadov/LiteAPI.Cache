using BenchmarkDotNet.Attributes;
using CacheBench.Caches;
using CacheBench.Workload;

namespace CacheBench.Benchmarks;

// Rigorous per-operation statistics (hit / miss / set) for each cache.
//   dotnet run -c Release -- bench
//   dotnet run -c Release -- bench --filter *Hit*
[Config(typeof(BenchConfig))]
[MemoryDiagnoser]
public class CacheBenchmarks
{
    [Params("MemoryCache", "FusionCache", "ActualLab.Fusion", "LiteAPI.Cache")]
    public string Cache { get; set; } = "MemoryCache";

    private Factory _factory = null!;
    private ICacheAdapter _adapter = null!;
    private string[] _keys = null!;
    private int _missCounter;

    [GlobalSetup]
    public async Task Setup()
    {
        _factory = new Factory(valueBytes: 256, delayMs: 0);
        _adapter = CacheRegistry.Create(Cache, _factory);
        _adapter.Reset();
        _keys = Keys.Generate(1000);
        foreach (var k in _keys)
            await _adapter.GetOrComputeAsync(k); // warm for Hit
    }

    [GlobalCleanup]
    public async Task Cleanup() => await _adapter.DisposeAsync();

    [Benchmark(Description = "Hit (warm get)")]
    public ValueTask<string> Hit() => _adapter.GetOrComputeAsync(_keys[0]);

    [Benchmark(Description = "Miss (get-or-compute)")]
    public ValueTask<string> Miss()
        => _adapter.GetOrComputeAsync("miss:" + Interlocked.Increment(ref _missCounter));

    [Benchmark(Description = "Set")]
    public async Task<bool> Set()
    {
        await _adapter.SetAsync(_keys[0], "value");
        return true;
    }
}
