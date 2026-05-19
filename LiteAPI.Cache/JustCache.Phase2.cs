using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace LiteAPI.Cache;

public static partial class JustCache
{
    #region Phase2: LRU / TTL / AOF / Binary Keys

    // LRU sizing
    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_set_max_items", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_set_max_items_win(UIntPtr maxItems);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_set_max_items", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_max_items_win(UIntPtr maxItems);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_set_max_items", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_set_max_items_linux(UIntPtr maxItems);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_set_max_items", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_max_items_linux(UIntPtr maxItems);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_set_max_items", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_set_max_items_mac(UIntPtr maxItems);
#else
    [DllImport(MacLib, EntryPoint = "cache_set_max_items", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_max_items_mac(UIntPtr maxItems);
#endif


    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_get_max_items", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial UIntPtr cache_get_max_items_win();
#else
    [DllImport(WindowsLib, EntryPoint = "cache_get_max_items", CallingConvention = CallingConvention.Cdecl)]
    private static extern UIntPtr cache_get_max_items_win();
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_get_max_items", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial UIntPtr cache_get_max_items_linux();
#else
    [DllImport(LinuxLib, EntryPoint = "cache_get_max_items", CallingConvention = CallingConvention.Cdecl)]
    private static extern UIntPtr cache_get_max_items_linux();
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_get_max_items", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial UIntPtr cache_get_max_items_mac();
#else
    [DllImport(MacLib, EntryPoint = "cache_get_max_items", CallingConvention = CallingConvention.Cdecl)]
    private static extern UIntPtr cache_get_max_items_mac();
#endif


    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_len", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial UIntPtr cache_len_win();
#else
    [DllImport(WindowsLib, EntryPoint = "cache_len", CallingConvention = CallingConvention.Cdecl)]
    private static extern UIntPtr cache_len_win();
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_len", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial UIntPtr cache_len_linux();
#else
    [DllImport(LinuxLib, EntryPoint = "cache_len", CallingConvention = CallingConvention.Cdecl)]
    private static extern UIntPtr cache_len_linux();
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_len", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial UIntPtr cache_len_mac();
#else
    [DllImport(MacLib, EntryPoint = "cache_len", CallingConvention = CallingConvention.Cdecl)]
    private static extern UIntPtr cache_len_mac();
#endif


    // TTL
    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_set_with_ttl", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_set_with_ttl_win(string key, byte[] val, UIntPtr len, ulong ttlMs);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_set_with_ttl", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_with_ttl_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len, ulong ttlMs);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_set_with_ttl", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_set_with_ttl_linux(string key, byte[] val, UIntPtr len, ulong ttlMs);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_set_with_ttl", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_with_ttl_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len, ulong ttlMs);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_set_with_ttl", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_set_with_ttl_mac(string key, byte[] val, UIntPtr len, ulong ttlMs);
#else
    [DllImport(MacLib, EntryPoint = "cache_set_with_ttl", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_with_ttl_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len, ulong ttlMs);
#endif


    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_expire", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int cache_expire_win(string key, ulong ttlMs);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_expire", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_expire_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, ulong ttlMs);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_expire", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int cache_expire_linux(string key, ulong ttlMs);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_expire", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_expire_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, ulong ttlMs);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_expire", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int cache_expire_mac(string key, ulong ttlMs);
#else
    [DllImport(MacLib, EntryPoint = "cache_expire", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_expire_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, ulong ttlMs);
#endif


    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_ttl", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial long cache_ttl_win(string key);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_ttl", CallingConvention = CallingConvention.Cdecl)]
    private static extern long cache_ttl_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_ttl", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial long cache_ttl_linux(string key);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_ttl", CallingConvention = CallingConvention.Cdecl)]
    private static extern long cache_ttl_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_ttl", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial long cache_ttl_mac(string key);
#else
    [DllImport(MacLib, EntryPoint = "cache_ttl", CallingConvention = CallingConvention.Cdecl)]
    private static extern long cache_ttl_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key);
#endif


    // AOF
    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_aof_enable", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int cache_aof_enable_win(string path);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_aof_enable", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_aof_enable_win([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_aof_enable", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int cache_aof_enable_linux(string path);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_aof_enable", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_aof_enable_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_aof_enable", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int cache_aof_enable_mac(string path);
#else
    [DllImport(MacLib, EntryPoint = "cache_aof_enable", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_aof_enable_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
#endif


    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_aof_disable", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_aof_disable_win();
#else
    [DllImport(WindowsLib, EntryPoint = "cache_aof_disable", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_aof_disable_win();
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_aof_disable", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_aof_disable_linux();
#else
    [DllImport(LinuxLib, EntryPoint = "cache_aof_disable", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_aof_disable_linux();
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_aof_disable", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_aof_disable_mac();
#else
    [DllImport(MacLib, EntryPoint = "cache_aof_disable", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_aof_disable_mac();
#endif


    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_aof_load", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int cache_aof_load_win(string path);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_aof_load", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_aof_load_win([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_aof_load", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int cache_aof_load_linux(string path);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_aof_load", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_aof_load_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_aof_load", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int cache_aof_load_mac(string path);
#else
    [DllImport(MacLib, EntryPoint = "cache_aof_load", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_aof_load_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
#endif


    // Binary-safe keys
    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_set_b", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_set_b_win(byte[] key, UIntPtr keyLen, byte[] val, UIntPtr len);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_set_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_b_win(byte[] key, UIntPtr keyLen, byte[] val, UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_set_b", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_set_b_linux(byte[] key, UIntPtr keyLen, byte[] val, UIntPtr len);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_set_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_b_linux(byte[] key, UIntPtr keyLen, byte[] val, UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_set_b", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_set_b_mac(byte[] key, UIntPtr keyLen, byte[] val, UIntPtr len);
#else
    [DllImport(MacLib, EntryPoint = "cache_set_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_b_mac(byte[] key, UIntPtr keyLen, byte[] val, UIntPtr len);
#endif


    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_get_b", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_get_b_win(byte[] key, UIntPtr keyLen, out UIntPtr len);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_get_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_get_b_win(byte[] key, UIntPtr keyLen, out UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_get_b", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_get_b_linux(byte[] key, UIntPtr keyLen, out UIntPtr len);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_get_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_get_b_linux(byte[] key, UIntPtr keyLen, out UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_get_b", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_get_b_mac(byte[] key, UIntPtr keyLen, out UIntPtr len);
#else
    [DllImport(MacLib, EntryPoint = "cache_get_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_get_b_mac(byte[] key, UIntPtr keyLen, out UIntPtr len);
#endif


    // Binary-safe zero-allocation get: caller provides destination buffer
    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_get_into_b", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe partial long cache_get_into_b_win(byte* key, UIntPtr keyLen, byte* dst, UIntPtr dstLen);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_get_into_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe long cache_get_into_b_win(byte* key, UIntPtr keyLen, byte* dst, UIntPtr dstLen);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_get_into_b", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe partial long cache_get_into_b_linux(byte* key, UIntPtr keyLen, byte* dst, UIntPtr dstLen);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_get_into_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe long cache_get_into_b_linux(byte* key, UIntPtr keyLen, byte* dst, UIntPtr dstLen);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_get_into_b", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe partial long cache_get_into_b_mac(byte* key, UIntPtr keyLen, byte* dst, UIntPtr dstLen);
#else
    [DllImport(MacLib, EntryPoint = "cache_get_into_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe long cache_get_into_b_mac(byte* key, UIntPtr keyLen, byte* dst, UIntPtr dstLen);
#endif

    // Read-only peek: same signature as `cache_get_into_b` but the native
    // side takes the shard's *read* lock and never touches LRU recency.
    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_peek_into_b", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe partial long cache_peek_into_b_win(byte* key, UIntPtr keyLen, byte* dst, UIntPtr dstLen);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_peek_into_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe long cache_peek_into_b_win(byte* key, UIntPtr keyLen, byte* dst, UIntPtr dstLen);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_peek_into_b", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe partial long cache_peek_into_b_linux(byte* key, UIntPtr keyLen, byte* dst, UIntPtr dstLen);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_peek_into_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe long cache_peek_into_b_linux(byte* key, UIntPtr keyLen, byte* dst, UIntPtr dstLen);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_peek_into_b", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe partial long cache_peek_into_b_mac(byte* key, UIntPtr keyLen, byte* dst, UIntPtr dstLen);
#else
    [DllImport(MacLib, EntryPoint = "cache_peek_into_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe long cache_peek_into_b_mac(byte* key, UIntPtr keyLen, byte* dst, UIntPtr dstLen);
#endif


    // Binary-safe zero-copy get: returns a lease handle + pointer/len
    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_get_lease_b", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe partial IntPtr cache_get_lease_b_win(byte* key, UIntPtr keyLen, out IntPtr outPtr, out UIntPtr outLen);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_get_lease_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe IntPtr cache_get_lease_b_win(byte* key, UIntPtr keyLen, out IntPtr outPtr, out UIntPtr outLen);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_get_lease_b", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe partial IntPtr cache_get_lease_b_linux(byte* key, UIntPtr keyLen, out IntPtr outPtr, out UIntPtr outLen);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_get_lease_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe IntPtr cache_get_lease_b_linux(byte* key, UIntPtr keyLen, out IntPtr outPtr, out UIntPtr outLen);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_get_lease_b", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe partial IntPtr cache_get_lease_b_mac(byte* key, UIntPtr keyLen, out IntPtr outPtr, out UIntPtr outLen);
#else
    [DllImport(MacLib, EntryPoint = "cache_get_lease_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe IntPtr cache_get_lease_b_mac(byte* key, UIntPtr keyLen, out IntPtr outPtr, out UIntPtr outLen);
#endif


    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_bytes_lease_free", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_bytes_lease_free_win(IntPtr handle);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_bytes_lease_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_bytes_lease_free_win(IntPtr handle);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_bytes_lease_free", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_bytes_lease_free_linux(IntPtr handle);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_bytes_lease_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_bytes_lease_free_linux(IntPtr handle);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_bytes_lease_free", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_bytes_lease_free_mac(IntPtr handle);
#else
    [DllImport(MacLib, EntryPoint = "cache_bytes_lease_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_bytes_lease_free_mac(IntPtr handle);
#endif


    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_remove_b", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_remove_b_win(byte[] key, UIntPtr keyLen);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_remove_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_remove_b_win(byte[] key, UIntPtr keyLen);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_remove_b", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_remove_b_linux(byte[] key, UIntPtr keyLen);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_remove_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_remove_b_linux(byte[] key, UIntPtr keyLen);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_remove_b", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_remove_b_mac(byte[] key, UIntPtr keyLen);
#else
    [DllImport(MacLib, EntryPoint = "cache_remove_b", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_remove_b_mac(byte[] key, UIntPtr keyLen);
#endif


    public static void SetMaxItems(int maxItems)
    {
        if (maxItems <= 0) maxItems = 1;
        var u = (UIntPtr)maxItems;

        if (_platform == Platform.Windows)
            cache_set_max_items_win(u);
        else if (_platform == Platform.Linux)
            cache_set_max_items_linux(u);
        else if (_platform == Platform.OSX)
            cache_set_max_items_mac(u);
        else
            throw new PlatformNotSupportedException();
    }

    public static int GetMaxItems()
    {
        UIntPtr u;
        if (_platform == Platform.Windows)
            u = cache_get_max_items_win();
        else if (_platform == Platform.Linux)
            u = cache_get_max_items_linux();
        else if (_platform == Platform.OSX)
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
            if (_platform == Platform.Windows)
                u = cache_len_win();
            else if (_platform == Platform.Linux)
                u = cache_len_linux();
            else if (_platform == Platform.OSX)
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

        if (_platform == Platform.Windows)
            cache_set_with_ttl_win(key, val, len, ttlMs);
        else if (_platform == Platform.Linux)
            cache_set_with_ttl_linux(key, val, len, ttlMs);
        else if (_platform == Platform.OSX)
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

        if (_platform == Platform.Windows)
            res = cache_expire_win(key, ttlMs);
        else if (_platform == Platform.Linux)
            res = cache_expire_linux(key, ttlMs);
        else if (_platform == Platform.OSX)
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

        if (_platform == Platform.Windows)
            return cache_ttl_win(key);
        if (_platform == Platform.Linux)
            return cache_ttl_linux(key);
        if (_platform == Platform.OSX)
            return cache_ttl_mac(key);

        throw new PlatformNotSupportedException();
    }

    public static bool EnableAof(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        int res;
        if (_platform == Platform.Windows)
            res = cache_aof_enable_win(path);
        else if (_platform == Platform.Linux)
            res = cache_aof_enable_linux(path);
        else if (_platform == Platform.OSX)
            res = cache_aof_enable_mac(path);
        else
            throw new PlatformNotSupportedException();

        return res != 0;
    }

    public static void DisableAof()
    {
        if (_platform == Platform.Windows)
            cache_aof_disable_win();
        else if (_platform == Platform.Linux)
            cache_aof_disable_linux();
        else if (_platform == Platform.OSX)
            cache_aof_disable_mac();
        else
            throw new PlatformNotSupportedException();
    }

    public static bool LoadAof(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        int res;
        if (_platform == Platform.Windows)
            res = cache_aof_load_win(path);
        else if (_platform == Platform.Linux)
            res = cache_aof_load_linux(path);
        else if (_platform == Platform.OSX)
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

        if (_platform == Platform.Windows)
            cache_set_b_win(key, klen, val, vlen);
        else if (_platform == Platform.Linux)
            cache_set_b_linux(key, klen, val, vlen);
        else if (_platform == Platform.OSX)
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

        if (_platform == Platform.Windows)
            ptr = cache_get_b_win(key, klen, out len);
        else if (_platform == Platform.Linux)
            ptr = cache_get_b_linux(key, klen, out len);
        else if (_platform == Platform.OSX)
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

            if (_platform == Platform.Windows)
                ret = cache_get_into_b_win(keyPtr, klen, dstPtr, dlen);
            else if (_platform == Platform.Linux)
                ret = cache_get_into_b_linux(keyPtr, klen, dstPtr, dlen);
            else if (_platform == Platform.OSX)
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

    /// <summary>
    /// Read-only sibling of <see cref="TryGet(byte[], Span{byte}, out int)"/>.
    /// Takes the shard's read lock instead of the write lock and skips
    /// LRU recency updates, so many threads can peek the same shard in
    /// parallel. Use it when the caller does not need an LRU promotion
    /// on read — a common case for hot-path lookups where the working
    /// set fits comfortably in the cache anyway.
    /// </summary>
    public static unsafe bool TryPeek(byte[] key, Span<byte> destination, out int written)
    {
        ArgumentNullException.ThrowIfNull(key);

        written = 0;

        long ret;
        fixed (byte* keyPtr = key)
        fixed (byte* dstPtr = destination)
        {
            var klen = (UIntPtr)key.Length;
            var dlen = (UIntPtr)destination.Length;

            if (_platform == Platform.Windows)
                ret = cache_peek_into_b_win(keyPtr, klen, dstPtr, dlen);
            else if (_platform == Platform.Linux)
                ret = cache_peek_into_b_linux(keyPtr, klen, dstPtr, dlen);
            else if (_platform == Platform.OSX)
                ret = cache_peek_into_b_mac(keyPtr, klen, dstPtr, dlen);
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

    public static bool TryPeek(byte[] key, byte[] destination, out int written)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return TryPeek(key, destination.AsSpan(), out written);
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

            if (_platform == Platform.Windows)
                cache_bytes_lease_free_win(_handle);
            else if (_platform == Platform.Linux)
                cache_bytes_lease_free_linux(_handle);
            else if (_platform == Platform.OSX)
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

            if (_platform == Platform.Windows)
                handle = cache_get_lease_b_win(keyPtr, klen, out outPtr, out outLen);
            else if (_platform == Platform.Linux)
                handle = cache_get_lease_b_linux(keyPtr, klen, out outPtr, out outLen);
            else if (_platform == Platform.OSX)
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
        if (_platform == Platform.Windows)
            cache_remove_b_win(key, klen);
        else if (_platform == Platform.Linux)
            cache_remove_b_linux(key, klen);
        else if (_platform == Platform.OSX)
            cache_remove_b_mac(key, klen);
        else
            throw new PlatformNotSupportedException();

        JustCacheEventSource.Log.ReportRemove();
    }

    #endregion
}
