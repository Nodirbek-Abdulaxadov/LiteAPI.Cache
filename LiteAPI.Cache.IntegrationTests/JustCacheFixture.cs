using System.Threading;
using LiteAPI.Cache;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace LiteAPI.Cache.IntegrationTests;

[CollectionDefinition("JustCacheCollection")]
public sealed class JustCacheCollection : ICollectionFixture<JustCacheFixture>
{
}

public sealed class JustCacheFixture : IDisposable
{
    private static int _initialized;

    public JustCacheFixture()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            JustCache.Initialize();
        }

        Reset();
    }

    public void Reset()
    {
        JustCache.ClearAll();
        JustCache.ClearNotifications();
        JustCache.SetMaxItems(1_000_000);
    }

    public void Dispose()
    {
        JustCache.ClearAll();
        JustCache.ClearNotifications();
    }
}
