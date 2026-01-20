namespace LiteAPI.Cache.Benchmarks;

internal static class JustCacheBootstrap
{
    private static readonly object Gate = new();
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (_initialized)
            return;

        lock (Gate)
        {
            if (_initialized)
                return;

            LiteAPI.Cache.JustCache.Initialize();
            _initialized = true;
        }
    }
}
