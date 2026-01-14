using System.Runtime.InteropServices;
using System.Text;

namespace LiteAPI.Cache;

public static partial class JustCache
{
    #region Sets (SADD/SISMEMBER)

    [DllImport(WindowsLib, EntryPoint = "cache_sadd", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_sadd_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len);

    [DllImport(LinuxLib, EntryPoint = "cache_sadd", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_sadd_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len);

    [DllImport(MacLib, EntryPoint = "cache_sadd", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_sadd_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len);


    [DllImport(WindowsLib, EntryPoint = "cache_sismember", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_sismember_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len);

    [DllImport(LinuxLib, EntryPoint = "cache_sismember", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_sismember_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len);

    [DllImport(MacLib, EntryPoint = "cache_sismember", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_sismember_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len);


    public static bool SAdd(string key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        var len = (UIntPtr)value.Length;
        int res;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            res = cache_sadd_win(key, value, len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            res = cache_sadd_linux(key, value, len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
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
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            res = cache_sismember_win(key, value, len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            res = cache_sismember_linux(key, value, len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
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
