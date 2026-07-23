using System.Collections.Concurrent;
using System.Collections.Generic;
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
    // cold key run the (expensive) factory N times. In front of a DB/HTTP call
    // under load that is the classic "cache stampede".
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
    // Hot-path cost: the GetOrAdd delegates are static (no per-miss closure for
    // them); the string fast path decodes straight off the native buffer; the
    // async fast path returns a synchronously-completed ValueTask (no Task
    // allocation on a hit). For a fully GC-free protected hit use the
    // byte[]-key + Span<byte> overload below — it copies into the caller's
    // buffer with zero managed allocation, exactly like TryGet.
    // ---------------------------------------------------------------------

    private static readonly ConcurrentDictionary<string, Lazy<byte[]>> _inflight =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> _inflightAsync =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<byte[], Lazy<byte[]>> _inflightBytes =
        new(ByteArrayComparer.Instance);

    #region string key, byte[] value (sync)

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

        // Static factory + tuple arg: no per-miss closure for the GetOrAdd delegate.
        var lazy = _inflight.GetOrAdd(
            key,
            static (k, arg) => new Lazy<byte[]>(
                () => ComputeAndStore(k, arg.factory, arg.ttl),
                LazyThreadSafetyMode.ExecutionAndPublication),
            (factory, ttl));

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

    private static byte[] ComputeAndStore(string key, Func<string, byte[]> factory, TimeSpan? ttl)
    {
        // Double-check: a prior flight may have populated the cache between our
        // fast-path miss and installing this Lazy.
        var cached = Get(key);
        if (cached is not null)
            return cached;

        var value = factory(key)
            ?? throw new InvalidOperationException(
                "GetOrCompute factory returned null; a cache value must be non-null.");

        if (ttl is { } t)
            SetWithTtl(key, value, t);
        else
            Set(key, value);

        return value;
    }

    #endregion

    #region string key, string value (sync)

    /// <summary>
    /// String-valued sibling of <see cref="GetOrCompute(string, Func{string, byte[]})"/>.
    /// Concurrent misses share a single factory execution; a hit is decoded
    /// straight off the native buffer (no intermediate managed byte[]).
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

        // Fast path: decode directly off the native buffer — one string
        // allocation, no intermediate byte[] (unlike Get() + Encoding.GetString).
        var existing = GetString(key);
        if (existing is not null)
            return existing;

        var bytes = GetOrComputeCore(key, k =>
        {
            var s = factory(k)
                ?? throw new InvalidOperationException(
                    "GetOrComputeString factory returned null; a cache value must be non-null.");
            return Encoding.UTF8.GetBytes(s);
        }, ttl);

        return Encoding.UTF8.GetString(bytes);
    }

    #endregion

    #region byte[] key + caller buffer (sync, zero-alloc hit)

    /// <summary>
    /// Zero-allocation stampede-protected get-or-compute. On a hit the value is
    /// copied straight into <paramref name="destination"/> with no managed
    /// allocation (exactly like <see cref="TryGet(byte[], Span{byte}, out int)"/>).
    /// On a miss, concurrent callers for the same key run <paramref name="factory"/>
    /// once; the result is cached and then copied into each caller's buffer.
    /// </summary>
    /// <returns>
    /// true and <paramref name="written"/> = bytes copied when the value fits;
    /// false and <paramref name="written"/> = required length when it does not.
    /// </returns>
    public static bool GetOrCompute(byte[] key, Span<byte> destination, Func<byte[]> factory, out int written)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        // Fast path: a hit copies into the caller buffer — zero managed alloc.
        if (TryGet(key, destination, out written))
            return true;

        // TryGet reports "buffer too small" as false with written = required
        // length; the key exists, so don't compute — surface the length.
        if (written > 0)
            return false;

        // Genuine miss: single-flight the compute, then copy into the buffer.
        var value = GetOrComputeBytesCore(key, factory);

        if (value.Length == 0)
        {
            written = 0;
            return true;
        }
        if (destination.Length < value.Length)
        {
            written = value.Length;
            return false;
        }

        value.AsSpan().CopyTo(destination);
        written = value.Length;
        return true;
    }

    private static byte[] GetOrComputeBytesCore(byte[] key, Func<byte[]> factory)
    {
        var lazy = _inflightBytes.GetOrAdd(
            key,
            static (k, f) => new Lazy<byte[]>(
                () => ComputeAndStoreBytes(k, f),
                LazyThreadSafetyMode.ExecutionAndPublication),
            factory);

        try
        {
            return lazy.Value;
        }
        finally
        {
            _inflightBytes.TryRemove(new KeyValuePair<byte[], Lazy<byte[]>>(key, lazy));
        }
    }

    private static byte[] ComputeAndStoreBytes(byte[] key, Func<byte[]> factory)
    {
        var existing = Get(key);
        if (existing is not null)
            return existing;

        var value = factory()
            ?? throw new InvalidOperationException(
                "GetOrCompute factory returned null; a cache value must be non-null.");

        Set(key, value);
        return value;
    }

    #endregion

    #region string key, byte[] value (async)

    /// <summary>
    /// Asynchronous single-flight get-or-compute. Concurrent misses of the same
    /// key await one shared <paramref name="factory"/> invocation, then all
    /// observe its result. A hit completes synchronously — the returned
    /// <see cref="ValueTask{TResult}"/> allocates no Task.
    /// <para>
    /// The shared computation runs under the <paramref name="cancellationToken"/>
    /// of the caller that started the flight; if that caller cancels, waiters on
    /// the same in-flight key observe the cancellation and may retry.
    /// </para>
    /// </summary>
    public static ValueTask<byte[]> GetOrComputeAsync(
        string key,
        Func<string, CancellationToken, Task<byte[]>> factory,
        CancellationToken cancellationToken = default)
        => GetOrComputeAsyncCore(key, factory, null, cancellationToken);

    /// <summary>
    /// TTL overload of
    /// <see cref="GetOrComputeAsync(string, Func{string, CancellationToken, Task{byte[]}}, CancellationToken)"/>.
    /// </summary>
    public static ValueTask<byte[]> GetOrComputeAsync(
        string key,
        Func<string, CancellationToken, Task<byte[]>> factory,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
        => GetOrComputeAsyncCore(key, factory, ttl, cancellationToken);

    private static ValueTask<byte[]> GetOrComputeAsyncCore(
        string key,
        Func<string, CancellationToken, Task<byte[]>> factory,
        TimeSpan? ttl,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        // Fast path: a hit completes synchronously with no Task allocation.
        var existing = Get(key);
        if (existing is not null)
            return new ValueTask<byte[]>(existing);

        if (cancellationToken.IsCancellationRequested)
            return new ValueTask<byte[]>(Task.FromCanceled<byte[]>(cancellationToken));

        var lazy = _inflightAsync.GetOrAdd(
            key,
            static (k, arg) => new Lazy<Task<byte[]>>(
                () => ComputeAndStoreAsync(k, arg.factory, arg.ttl, arg.cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication),
            (factory, ttl, cancellationToken));

        return new ValueTask<byte[]>(AwaitAndRetireAsync(key, lazy));
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

    #endregion

    // Content-based equality for byte[] keys in the zero-alloc in-flight map.
    // Only touched on a miss (the hit path never enters the map), so hashing
    // the whole key here is off the hot path.
    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();

        public bool Equals(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null || x.Length != y.Length) return false;
            return x.AsSpan().SequenceEqual(y);
        }

        public int GetHashCode(byte[] obj)
        {
            var hc = new HashCode();
            hc.AddBytes(obj);
            return hc.ToHashCode();
        }
    }
}
