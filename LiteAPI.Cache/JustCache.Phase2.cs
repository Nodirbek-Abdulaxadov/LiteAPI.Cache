using System.Runtime.InteropServices;

namespace LiteAPI.Cache;

public static partial class JustCache
{
    #region Phase2: LRU / TTL / AOF / Binary Keys

    // LRU sizing
    [DllImport(WindowsLib, EntryPoint = "cache_set_max_items", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_max_items_win(UIntPtr maxItems);

    [DllImport(LinuxLib, EntryPoint = "cache_set_max_items", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_max_items_linux(UIntPtr maxItems);

    [DllImport(MacLib, EntryPoint = "cache_set_max_items", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_max_items_mac(UIntPtr maxItems);


    [DllImport(WindowsLib, EntryPoint = "cache_get_max_items", CallingConvention = CallingConvention.Cdecl)]
    private static extern UIntPtr cache_get_max_items_win();

    [DllImport(LinuxLib, EntryPoint = "cache_get_max_items", CallingConvention = CallingConvention.Cdecl)]
    private static extern UIntPtr cache_get_max_items_linux();

    [DllImport(MacLib, EntryPoint = "cache_get_max_items", CallingConvention = CallingConvention.Cdecl)]
    private static extern UIntPtr cache_get_max_items_mac();


    [DllImport(WindowsLib, EntryPoint = "cache_len", CallingConvention = CallingConvention.Cdecl)]
    private static extern UIntPtr cache_len_win();

    [DllImport(LinuxLib, EntryPoint = "cache_len", CallingConvention = CallingConvention.Cdecl)]
    private static extern UIntPtr cache_len_linux();

    [DllImport(MacLib, EntryPoint = "cache_len", CallingConvention = CallingConvention.Cdecl)]
    private static extern UIntPtr cache_len_mac();


    // TTL
    [DllImport(WindowsLib, EntryPoint = "cache_set_with_ttl", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_with_ttl_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len, ulong ttlMs);

    [DllImport(LinuxLib, EntryPoint = "cache_set_with_ttl", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_with_ttl_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len, ulong ttlMs);

    [DllImport(MacLib, EntryPoint = "cache_set_with_ttl", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_with_ttl_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len, ulong ttlMs);


    [DllImport(WindowsLib, EntryPoint = "cache_expire", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_expire_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, ulong ttlMs);

    [DllImport(LinuxLib, EntryPoint = "cache_expire", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_expire_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, ulong ttlMs);

    [DllImport(MacLib, EntryPoint = "cache_expire", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_expire_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, ulong ttlMs);


    [DllImport(WindowsLib, EntryPoint = "cache_ttl", CallingConvention = CallingConvention.Cdecl)]
    private static extern long cache_ttl_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key);

    [DllImport(LinuxLib, EntryPoint = "cache_ttl", CallingConvention = CallingConvention.Cdecl)]
    private static extern long cache_ttl_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key);

    [DllImport(MacLib, EntryPoint = "cache_ttl", CallingConvention = CallingConvention.Cdecl)]
    private static extern long cache_ttl_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key);


    // AOF
    [DllImport(WindowsLib, EntryPoint = "cache_aof_enable", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_aof_enable_win([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(LinuxLib, EntryPoint = "cache_aof_enable", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_aof_enable_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(MacLib, EntryPoint = "cache_aof_enable", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_aof_enable_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string path);


    [DllImport(WindowsLib, EntryPoint = "cache_aof_disable", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_aof_disable_win();

    [DllImport(LinuxLib, EntryPoint = "cache_aof_disable", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_aof_disable_linux();

    [DllImport(MacLib, EntryPoint = "cache_aof_disable", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_aof_disable_mac();


    [DllImport(WindowsLib, EntryPoint = "cache_aof_load", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_aof_load_win([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(LinuxLib, EntryPoint = "cache_aof_load", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_aof_load_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(MacLib, EntryPoint = "cache_aof_load", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_aof_load_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string path);


    // Binary-safe keys
    [DllImport(WindowsLib, EntryPoint = "cache_set_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_b_win(byte[] key, UIntPtr keyLen, byte[] val, UIntPtr len);

    [DllImport(LinuxLib, EntryPoint = "cache_set_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_b_linux(byte[] key, UIntPtr keyLen, byte[] val, UIntPtr len);

    [DllImport(MacLib, EntryPoint = "cache_set_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_b_mac(byte[] key, UIntPtr keyLen, byte[] val, UIntPtr len);


    [DllImport(WindowsLib, EntryPoint = "cache_get_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_get_b_win(byte[] key, UIntPtr keyLen, out UIntPtr len);

    [DllImport(LinuxLib, EntryPoint = "cache_get_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_get_b_linux(byte[] key, UIntPtr keyLen, out UIntPtr len);

    [DllImport(MacLib, EntryPoint = "cache_get_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_get_b_mac(byte[] key, UIntPtr keyLen, out UIntPtr len);


    // Binary-safe zero-allocation get: caller provides destination buffer
    [DllImport(WindowsLib, EntryPoint = "cache_get_into_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe long cache_get_into_b_win(byte* key, UIntPtr keyLen, byte* dst, UIntPtr dstLen);

    [DllImport(LinuxLib, EntryPoint = "cache_get_into_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe long cache_get_into_b_linux(byte* key, UIntPtr keyLen, byte* dst, UIntPtr dstLen);

    [DllImport(MacLib, EntryPoint = "cache_get_into_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe long cache_get_into_b_mac(byte* key, UIntPtr keyLen, byte* dst, UIntPtr dstLen);


    // Binary-safe zero-copy get: returns a lease handle + pointer/len
    [DllImport(WindowsLib, EntryPoint = "cache_get_lease_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe IntPtr cache_get_lease_b_win(byte* key, UIntPtr keyLen, out IntPtr outPtr, out UIntPtr outLen);

    [DllImport(LinuxLib, EntryPoint = "cache_get_lease_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe IntPtr cache_get_lease_b_linux(byte* key, UIntPtr keyLen, out IntPtr outPtr, out UIntPtr outLen);

    [DllImport(MacLib, EntryPoint = "cache_get_lease_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe IntPtr cache_get_lease_b_mac(byte* key, UIntPtr keyLen, out IntPtr outPtr, out UIntPtr outLen);


    [DllImport(WindowsLib, EntryPoint = "cache_bytes_lease_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_bytes_lease_free_win(IntPtr handle);

    [DllImport(LinuxLib, EntryPoint = "cache_bytes_lease_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_bytes_lease_free_linux(IntPtr handle);

    [DllImport(MacLib, EntryPoint = "cache_bytes_lease_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_bytes_lease_free_mac(IntPtr handle);


    [DllImport(WindowsLib, EntryPoint = "cache_remove_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_remove_b_win(byte[] key, UIntPtr keyLen);

    [DllImport(LinuxLib, EntryPoint = "cache_remove_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_remove_b_linux(byte[] key, UIntPtr keyLen);

    [DllImport(MacLib, EntryPoint = "cache_remove_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_remove_b_mac(byte[] key, UIntPtr keyLen);


    public static void SetMaxItems(int maxItems)
    {
        if (maxItems <= 0) maxItems = 1;
        var u = (UIntPtr)maxItems;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            cache_set_max_items_win(u);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            cache_set_max_items_linux(u);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            cache_set_max_items_mac(u);
        else
            throw new PlatformNotSupportedException();
    }

    public static int GetMaxItems()
    {
        UIntPtr u;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            u = cache_get_max_items_win();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            u = cache_get_max_items_linux();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            u = cache_get_max_items_mac();
        else
            throw new PlatformNotSupportedException();

        return (int)u;
    }

    public static int Count
    {
        get
        {
            UIntPtr u;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                u = cache_len_win();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                u = cache_len_linux();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                u = cache_len_mac();
            else
                throw new PlatformNotSupportedException();

            return (int)u;
        }
    }

    public static void SetWithTtl(string key, byte[] val, TimeSpan ttl)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(val);

        ulong ttlMs = (ulong)Math.Max(0, (long)ttl.TotalMilliseconds);
        var len = (UIntPtr)val.Length;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            cache_set_with_ttl_win(key, val, len, ttlMs);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            cache_set_with_ttl_linux(key, val, len, ttlMs);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            cache_set_with_ttl_mac(key, val, len, ttlMs);
        else
            throw new PlatformNotSupportedException();
    }

    public static void SetStringWithTtl(string key, string val, TimeSpan ttl)
    {
        ArgumentNullException.ThrowIfNull(val);
        SetWithTtl(key, System.Text.Encoding.UTF8.GetBytes(val), ttl);
    }

    public static bool Expire(string key, TimeSpan ttl)
    {
        ArgumentNullException.ThrowIfNull(key);

        ulong ttlMs = (ulong)Math.Max(0, (long)ttl.TotalMilliseconds);
        int res;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            res = cache_expire_win(key, ttlMs);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            res = cache_expire_linux(key, ttlMs);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            res = cache_expire_mac(key, ttlMs);
        else
            throw new PlatformNotSupportedException();

        return res != 0;
    }

    // Redis-style TTL semantics:
    // -2: key does not exist
    // -1: no expiry
    // >=0: milliseconds remaining
    public static long TtlMs(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return cache_ttl_win(key);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return cache_ttl_linux(key);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return cache_ttl_mac(key);

        throw new PlatformNotSupportedException();
    }

    public static bool EnableAof(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        int res;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            res = cache_aof_enable_win(path);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            res = cache_aof_enable_linux(path);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            res = cache_aof_enable_mac(path);
        else
            throw new PlatformNotSupportedException();

        return res != 0;
    }

    public static void DisableAof()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            cache_aof_disable_win();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            cache_aof_disable_linux();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            cache_aof_disable_mac();
        else
            throw new PlatformNotSupportedException();
    }

    public static bool LoadAof(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        int res;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            res = cache_aof_load_win(path);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            res = cache_aof_load_linux(path);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            res = cache_aof_load_mac(path);
        else
            throw new PlatformNotSupportedException();

        return res != 0;
    }

    public static void Set(byte[] key, byte[] val)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(val);

        var klen = (UIntPtr)key.Length;
        var vlen = (UIntPtr)val.Length;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            cache_set_b_win(key, klen, val, vlen);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            cache_set_b_linux(key, klen, val, vlen);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            cache_set_b_mac(key, klen, val, vlen);
        else
            throw new PlatformNotSupportedException();

        JustCacheEventSource.Log.ReportSet(val.Length);
    }

    public static byte[]? Get(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);

        UIntPtr len;
        IntPtr ptr;
        var klen = (UIntPtr)key.Length;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            ptr = cache_get_b_win(key, klen, out len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            ptr = cache_get_b_linux(key, klen, out len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            ptr = cache_get_b_mac(key, klen, out len);
        else
            throw new PlatformNotSupportedException();

        if (ptr == IntPtr.Zero || len == UIntPtr.Zero)
        {
            JustCacheEventSource.Log.ReportGetMiss();
            return null;
        }

        var result = CopyAndFree(ptr, len);
        JustCacheEventSource.Log.ReportGetHit(result.Length);
        return result;
    }

    // Caller-buffer API for GC-free hit path.
    // Semantics:
    // - returns true when value copied into destination (written = bytes copied)
    // - returns false when key missing (written = 0)
    // - returns false when destination too small (written = required length)
    public static unsafe bool TryGet(byte[] key, Span<byte> destination, out int written)
    {
        ArgumentNullException.ThrowIfNull(key);

        written = 0;

        long ret;
        fixed (byte* keyPtr = key)
        fixed (byte* dstPtr = destination)
        {
            var klen = (UIntPtr)key.Length;
            var dlen = (UIntPtr)destination.Length;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                ret = cache_get_into_b_win(keyPtr, klen, dstPtr, dlen);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                ret = cache_get_into_b_linux(keyPtr, klen, dstPtr, dlen);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                ret = cache_get_into_b_mac(keyPtr, klen, dstPtr, dlen);
            else
                throw new PlatformNotSupportedException();
        }

        if (ret == -1)
        {
            JustCacheEventSource.Log.ReportGetMiss();
            return false;
        }

        if (ret < 0)
        {
            written = checked((int)(-ret));
            JustCacheEventSource.Log.ReportGetHit(written);
            return false;
        }

        written = checked((int)ret);
        JustCacheEventSource.Log.ReportGetHit(written);
        return true;
    }

    public static bool TryGet(byte[] key, byte[] destination, out int written)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return TryGet(key, destination.AsSpan(), out written);
    }

    public readonly ref struct BytesLease
    {
        private readonly IntPtr _handle;
        private readonly IntPtr _ptr;

        public int Length { get; }
        public bool IsValid => _handle != IntPtr.Zero;

        internal BytesLease(IntPtr handle, IntPtr ptr, int length)
        {
            _handle = handle;
            _ptr = ptr;
            Length = length;
        }

        public unsafe ReadOnlySpan<byte> Span
        {
            get
            {
                if (_handle == IntPtr.Zero || _ptr == IntPtr.Zero || Length <= 0)
                    return ReadOnlySpan<byte>.Empty;
                return new ReadOnlySpan<byte>((void*)_ptr, Length);
            }
        }

        public void Dispose()
        {
            if (_handle == IntPtr.Zero)
                return;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                cache_bytes_lease_free_win(_handle);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                cache_bytes_lease_free_linux(_handle);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                cache_bytes_lease_free_mac(_handle);
            else
                throw new PlatformNotSupportedException();
        }
    }

    // Zero-copy read API. Returned lease must be disposed.
    // - Missing key => returns default lease (IsValid=false)
    // - Existing key with empty value => IsValid=true, Span empty
    public static unsafe BytesLease GetLease(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);

        IntPtr outPtr;
        UIntPtr outLen;
        IntPtr handle;

        fixed (byte* keyPtr = key)
        {
            var klen = (UIntPtr)key.Length;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                handle = cache_get_lease_b_win(keyPtr, klen, out outPtr, out outLen);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                handle = cache_get_lease_b_linux(keyPtr, klen, out outPtr, out outLen);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                handle = cache_get_lease_b_mac(keyPtr, klen, out outPtr, out outLen);
            else
                throw new PlatformNotSupportedException();
        }

        if (handle == IntPtr.Zero)
        {
            JustCacheEventSource.Log.ReportGetMiss();
            return default;
        }

        var length = checked((int)outLen);
        JustCacheEventSource.Log.ReportGetHit(length);
        return new BytesLease(handle, outPtr, length);
    }

    public static void Remove(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var klen = (UIntPtr)key.Length;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            cache_remove_b_win(key, klen);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            cache_remove_b_linux(key, klen);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            cache_remove_b_mac(key, klen);
        else
            throw new PlatformNotSupportedException();

        JustCacheEventSource.Log.ReportRemove();
    }

    #endregion
}
