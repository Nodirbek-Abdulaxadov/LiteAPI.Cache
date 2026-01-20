using BenchmarkDotNet.Attributes;
using StackExchange.Redis;

namespace LiteAPI.Cache.Benchmarks;

[MemoryDiagnoser]
public class RedisLikeStructuresBenchmarks
{
    public IEnumerable<CacheBackend> Backends =>
        RedisBenchClient.IsAvailable()
            ? new[] { CacheBackend.JustCache, CacheBackend.Redis }
            : new[] { CacheBackend.JustCache };

    [ParamsSource(nameof(Backends))]
    public CacheBackend Backend { get; set; }

    private RedisBenchClient? _redis;

    private string _prefix = null!;

    private string _hashKey = null!;
    private string _hashField = null!;
    private string _hashValue = null!;

    private string _listKey = null!;
    private string _listValue = null!;

    private string _setKey = null!;
    private string _setMember = null!;

    private string _zsetKey = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _prefix = $"bench:{Guid.NewGuid():N}:";

        _hashKey = _prefix + "h";
        _hashField = "f";
        _hashValue = "v";

        _listKey = _prefix + "l";
        _listValue = "x";

        _setKey = _prefix + "s";
        _setMember = "m";

        _zsetKey = _prefix + "z";

        switch (Backend)
        {
            case CacheBackend.JustCache:
                JustCacheBootstrap.EnsureInitialized();
                LiteAPI.Cache.JustCache.ClearAll();
                break;

            case CacheBackend.Redis:
                _redis = RedisBenchClient.ConnectOrThrow();
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _redis?.Dispose();
    }

    [IterationSetup(Target = nameof(Hash_HSet_HGet))]
    public void IterationSetupHash()
    {
        switch (Backend)
        {
            case CacheBackend.JustCache:
                LiteAPI.Cache.JustCache.HSetString(_hashKey, _hashField, _hashValue);
                break;
            case CacheBackend.Redis:
                _redis!.Db.HashSet(_hashKey, _hashField, _hashValue);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [Benchmark]
    public int Hash_HSet_HGet()
    {
        switch (Backend)
        {
            case CacheBackend.JustCache:
                LiteAPI.Cache.JustCache.HSetString(_hashKey, _hashField, _hashValue);
                return LiteAPI.Cache.JustCache.HGetString(_hashKey, _hashField)?.Length ?? 0;

            case CacheBackend.Redis:
            {
                _redis!.Db.HashSet(_hashKey, _hashField, _hashValue);
                RedisValue v = _redis.Db.HashGet(_hashKey, _hashField);
                return v.HasValue ? (int)v.Length() : 0;
            }

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [IterationSetup(Target = nameof(List_LPush_RPop))]
    public void IterationSetupList()
    {
        switch (Backend)
        {
            case CacheBackend.JustCache:
                LiteAPI.Cache.JustCache.Remove(_listKey);
                break;
            case CacheBackend.Redis:
                _redis!.Db.KeyDelete(_listKey);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [Benchmark]
    public int List_LPush_RPop()
    {
        switch (Backend)
        {
            case CacheBackend.JustCache:
                LiteAPI.Cache.JustCache.LPushString(_listKey, _listValue);
                return LiteAPI.Cache.JustCache.RPopString(_listKey)?.Length ?? 0;

            case CacheBackend.Redis:
            {
                _redis!.Db.ListLeftPush(_listKey, _listValue);
                RedisValue v = _redis.Db.ListRightPop(_listKey);
                return v.HasValue ? (int)v.Length() : 0;
            }

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [IterationSetup(Target = nameof(Set_SAdd_SIsMember))]
    public void IterationSetupSet()
    {
        switch (Backend)
        {
            case CacheBackend.JustCache:
                LiteAPI.Cache.JustCache.Remove(_setKey);
                break;
            case CacheBackend.Redis:
                _redis!.Db.KeyDelete(_setKey);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [Benchmark]
    public bool Set_SAdd_SIsMember()
    {
        switch (Backend)
        {
            case CacheBackend.JustCache:
                _ = LiteAPI.Cache.JustCache.SAddString(_setKey, _setMember);
                return LiteAPI.Cache.JustCache.SIsMemberString(_setKey, _setMember);

            case CacheBackend.Redis:
                _ = _redis!.Db.SetAdd(_setKey, _setMember);
                return _redis.Db.SetContains(_setKey, _setMember);

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [IterationSetup(Target = nameof(SortedSet_ZAdd_ZRange))]
    public void IterationSetupZSet()
    {
        switch (Backend)
        {
            case CacheBackend.JustCache:
                LiteAPI.Cache.JustCache.Remove(_zsetKey);
                break;
            case CacheBackend.Redis:
                _redis!.Db.KeyDelete(_zsetKey);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    [Benchmark]
    public int SortedSet_ZAdd_ZRange()
    {
        switch (Backend)
        {
            case CacheBackend.JustCache:
                LiteAPI.Cache.JustCache.ZAdd(_zsetKey, 1, "a");
                LiteAPI.Cache.JustCache.ZAdd(_zsetKey, 2, "b");
                LiteAPI.Cache.JustCache.ZAdd(_zsetKey, 3, "c");
                return LiteAPI.Cache.JustCache.ZRange(_zsetKey, 0, -1).Count;

            case CacheBackend.Redis:
                _redis!.Db.SortedSetAdd(_zsetKey, "a", 1);
                _redis.Db.SortedSetAdd(_zsetKey, "b", 2);
                _redis.Db.SortedSetAdd(_zsetKey, "c", 3);
                return _redis.Db.SortedSetRangeByRank(_zsetKey, 0, -1).Length;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
