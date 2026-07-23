using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LiteAPI.Cache;

public static partial class JustCache
{
    // ---------------------------------------------------------------------
    // Stampede protection — single-flight get-or-compute.
    //
    // The plain get-or-compute pattern (TryGet -> miss -> factory -> Set) has
    // no coordination between threads, so N concurrent misses of the *same*
    // cold key run the (expensive) factory N times. In front of a DB/HTTP
    // call under load that is the classic "cache stampede".
    //
    // GetOrCompute / GetOrComputeAsync collapse a burst of concurrent misses
    // for one key onto a single factory execution; every caller in the burst
    // observes that one result. Keys already in the cache take the fast path
    // and never touch the coordination map.
    //
    // Mechanism: a per-key Lazy<T> held in a ConcurrentDictionary. The first
    // caller to miss installs the Lazy; concurrent callers retrieve the same
    // instance and block on its single value-factory (ExecutionAndPublication).
    // The slot is retired in a finally so the map stays bounded and a thrown
    // factory never poisons later calls (Lazy caches exceptions). A second,
    // in-flight Get() closes the small window where a prior flight finishes
    // between another caller's fast-path miss and its GetOrAdd.
    //
    // Note: these are convenience / miss-path APIs and return an allocated
    // byte[] (or string) by contract. The zero-allocation read path remains
    // TryGet(byte[], Span<byte>) and GetLease.
    // ---------------------------------------------------------------------

    private static readonly ConcurrentDictionary<string, Lazy<byte[]>> _inflight =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> _inflightAsync =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or computes it with
    /// <paramref name="factory"/> and caches it. Concurrent misses of the same
    /// key run the factory <b>once</b> (single-flight); every caller in the
    /// burst receives that one result. This is the cache-stampede protection
    /// the plain TryGet/Set pattern lacks.
    /// </summary>
    public static byte[] GetOrCompute(string key, Func<string, byte[]> factory)
        => GetOrComputeCore(key, factory, null);

    /// <summary>
    /// TTL overload of <see cref="GetOrCompute(string, Func{string, byte[]})"/>:
    /// a freshly computed value is stored with an absolute <paramref name="ttl"/>.
    /// A value already present is returned as-is (its existing TTL is untouched).
    /// </summary>
    public static byte[] GetOrCompute(string key, Func<string, byte[]> factory, TimeSpan ttl)
        => GetOrComputeCore(key, factory, ttl);

    private static byte[] GetOrComputeCore(string key, Func<string, byte[]> factory, TimeSpan? ttl)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        // Fast path: already cached — never allocate a Lazy or touch the map.
        var existing = Get(key);
        if (existing is not null)
            return existing;

        var lazy = _inflight.GetOrAdd(key, k => new Lazy<byte[]>(
            () =>
            {
                // Double-check: a prior flight may have populated the cache
                // between our fast-path miss and installing this Lazy.
                var cached = Get(k);
                if (cached is not null)
                    return cached;

                var value = factory(k)
                    ?? throw new InvalidOperationException(
                        "GetOrCompute factory returned null; a cache value must be non-null.");

                if (ttl is { } t)
                    SetWithTtl(k, value, t);
                else
                    Set(k, value);

                return value;
            },
            LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return lazy.Value;
        }
        finally
        {
            // Retire only *our* Lazy — a later miss may already have installed
            // a fresh one for the same key.
            _inflight.TryRemove(new KeyValuePair<string, Lazy<byte[]>>(key, lazy));
        }
    }

    /// <summary>
    /// String-valued sibling of <see cref="GetOrCompute(string, Func{string, byte[]})"/>.
    /// The factory result is UTF-8 encoded on store and decoded on return.
    /// Concurrent misses share a single factory execution.
    /// </summary>
    public static string GetOrComputeString(string key, Func<string, string> factory)
        => GetOrComputeStringCore(key, factory, null);

    /// <summary>
    /// TTL overload of <see cref="GetOrComputeString(string, Func{string, string})"/>.
    /// </summary>
    public static string GetOrComputeString(string key, Func<string, string> factory, TimeSpan ttl)
        => GetOrComputeStringCore(key, factory, ttl);

    private static string GetOrComputeStringCore(string key, Func<string, string> factory, TimeSpan? ttl)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var bytes = GetOrComputeCore(key, k =>
        {
            var s = factory(k)
                ?? throw new InvalidOperationException(
                    "GetOrComputeString factory returned null; a cache value must be non-null.");
            return Encoding.UTF8.GetBytes(s);
        }, ttl);

        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Asynchronous single-flight get-or-compute. Concurrent misses of the same
    /// key await one shared <paramref name="factory"/> invocation, then all
    /// observe its result.
    /// <para>
    /// The shared computation runs under the <paramref name="cancellationToken"/>
    /// of the caller that started the flight; if that caller cancels, waiters on
    /// the same in-flight key observe the cancellation and may retry.
    /// </para>
    /// </summary>
    public static Task<byte[]> GetOrComputeAsync(
        string key,
        Func<string, CancellationToken, Task<byte[]>> factory,
        CancellationToken cancellationToken = default)
        => GetOrComputeAsyncCore(key, factory, null, cancellationToken);

    /// <summary>
    /// TTL overload of
    /// <see cref="GetOrComputeAsync(string, Func{string, CancellationToken, Task{byte[]}}, CancellationToken)"/>.
    /// </summary>
    public static Task<byte[]> GetOrComputeAsync(
        string key,
        Func<string, CancellationToken, Task<byte[]>> factory,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
        => GetOrComputeAsyncCore(key, factory, ttl, cancellationToken);

    private static Task<byte[]> GetOrComputeAsyncCore(
        string key,
        Func<string, CancellationToken, Task<byte[]>> factory,
        TimeSpan? ttl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        var existing = Get(key);
        if (existing is not null)
            return Task.FromResult(existing);

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<byte[]>(cancellationToken);

        var lazy = _inflightAsync.GetOrAdd(key, k => new Lazy<Task<byte[]>>(
            () => ComputeAndStoreAsync(k, factory, ttl, cancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication));

        return AwaitAndRetireAsync(key, lazy);
    }

    private static async Task<byte[]> ComputeAndStoreAsync(
        string key,
        Func<string, CancellationToken, Task<byte[]>> factory,
        TimeSpan? ttl,
        CancellationToken cancellationToken)
    {
        // Double-check under the flight (see the sync path for why).
        var cached = Get(key);
        if (cached is not null)
            return cached;

        var value = await factory(key, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "GetOrComputeAsync factory returned null; a cache value must be non-null.");

        if (ttl is { } t)
            SetWithTtl(key, value, t);
        else
            Set(key, value);

        return value;
    }

    private static async Task<byte[]> AwaitAndRetireAsync(string key, Lazy<Task<byte[]>> lazy)
    {
        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        finally
        {
            _inflightAsync.TryRemove(new KeyValuePair<string, Lazy<Task<byte[]>>>(key, lazy));
        }
    }
}
