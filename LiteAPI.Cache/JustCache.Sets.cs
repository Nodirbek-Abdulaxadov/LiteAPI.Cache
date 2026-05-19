using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;

namespace LiteAPI.Cache;

public static partial class JustCache
{
    #region Sets (SADD/SISMEMBER)

    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_sadd", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int cache_sadd_win(string key, byte[] val, UIntPtr len);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_sadd", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_sadd_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_sadd", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int cache_sadd_linux(string key, byte[] val, UIntPtr len);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_sadd", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_sadd_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_sadd", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int cache_sadd_mac(string key, byte[] val, UIntPtr len);
#else
    [DllImport(MacLib, EntryPoint = "cache_sadd", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_sadd_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len);
#endif


    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_sismember", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int cache_sismember_win(string key, byte[] val, UIntPtr len);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_sismember", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_sismember_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_sismember", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int cache_sismember_linux(string key, byte[] val, UIntPtr len);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_sismember", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_sismember_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_sismember", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int cache_sismember_mac(string key, byte[] val, UIntPtr len);
#else
    [DllImport(MacLib, EntryPoint = "cache_sismember", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_sismember_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len);
#endif


    public static bool SAdd(string key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        var len = (UIntPtr)value.Length;
        int res;
        if (_platform == Platform.Windows)
            res = cache_sadd_win(key, value, len);
        else if (_platform == Platform.Linux)
            res = cache_sadd_linux(key, value, len);
        else if (_platform == Platform.OSX)
            res = cache_sadd_mac(key, value, len);
        else
            throw new PlatformNotSupportedException();

        return res != 0;
    }

    public static bool SAddString(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return SAdd(key, Encoding.UTF8.GetBytes(value));
    }

    public static bool SIsMember(string key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        var len = (UIntPtr)value.Length;
        int res;
        if (_platform == Platform.Windows)
            res = cache_sismember_win(key, value, len);
        else if (_platform == Platform.Linux)
            res = cache_sismember_linux(key, value, len);
        else if (_platform == Platform.OSX)
            res = cache_sismember_mac(key, value, len);
        else
            throw new PlatformNotSupportedException();

        return res != 0;
    }

    public static bool SIsMemberString(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return SIsMember(key, Encoding.UTF8.GetBytes(value));
    }

    #endregion
}
