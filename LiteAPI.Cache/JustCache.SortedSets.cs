using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;

namespace LiteAPI.Cache;

public static partial class JustCache
{
    #region Sorted Sets (ZADD/ZRANGE)

    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_zadd", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_zadd_win(string key, double score, string member);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_zadd", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_zadd_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, double score, [MarshalAs(UnmanagedType.LPUTF8Str)] string member);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_zadd", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_zadd_linux(string key, double score, string member);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_zadd", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_zadd_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, double score, [MarshalAs(UnmanagedType.LPUTF8Str)] string member);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_zadd", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_zadd_mac(string key, double score, string member);
#else
    [DllImport(MacLib, EntryPoint = "cache_zadd", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_zadd_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, double score, [MarshalAs(UnmanagedType.LPUTF8Str)] string member);
#endif


    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_zrange", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_zrange_win(string key, int start, int end, out UIntPtr len);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_zrange", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_zrange_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, int start, int end, out UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_zrange", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_zrange_linux(string key, int start, int end, out UIntPtr len);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_zrange", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_zrange_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, int start, int end, out UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_zrange", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_zrange_mac(string key, int start, int end, out UIntPtr len);
#else
    [DllImport(MacLib, EntryPoint = "cache_zrange", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_zrange_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, int start, int end, out UIntPtr len);
#endif


    public static void ZAdd(string key, double score, string member)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(member);

        if (_platform == Platform.Windows)
            cache_zadd_win(key, score, member);
        else if (_platform == Platform.Linux)
            cache_zadd_linux(key, score, member);
        else if (_platform == Platform.OSX)
            cache_zadd_mac(key, score, member);
        else
            throw new PlatformNotSupportedException();
    }

    public static List<string> ZRange(string key, int start, int end)
    {
        UIntPtr len;
        IntPtr ptr;

        if (_platform == Platform.Windows)
            ptr = cache_zrange_win(key, start, end, out len);
        else if (_platform == Platform.Linux)
            ptr = cache_zrange_linux(key, start, end, out len);
        else if (_platform == Platform.OSX)
            ptr = cache_zrange_mac(key, start, end, out len);
        else
            throw new PlatformNotSupportedException();

        if (ptr == IntPtr.Zero || len == UIntPtr.Zero)
            return new List<string>();

        var blob = CopyAndFree(ptr, len);
        return ParseZRangeBlob(blob);
    }

    private static List<string> ParseZRangeBlob(byte[] blob)
    {
        // format: [Count (u32)] [MemberLen (u32)] [Member] ...
        var result = new List<string>();
        if (blob.Length < 4)
            return result;

        int offset = 0;
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(offset, 4));
        offset += 4;

        result.Capacity = (int)Math.Min(count, int.MaxValue);

        for (uint i = 0; i < count; i++)
        {
            if (offset + 4 > blob.Length) break;
            uint mlen = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(offset, 4));
            offset += 4;
            if (offset + mlen > blob.Length) break;

            string member = Encoding.UTF8.GetString(blob, offset, (int)mlen);
            offset += (int)mlen;
            result.Add(member);
        }

        return result;
    }

    #endregion
}
