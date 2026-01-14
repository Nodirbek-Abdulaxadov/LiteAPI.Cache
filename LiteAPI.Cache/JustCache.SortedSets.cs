using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace LiteAPI.Cache;

public static partial class JustCache
{
    #region Sorted Sets (ZADD/ZRANGE)

    [DllImport(WindowsLib, EntryPoint = "cache_zadd", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_zadd_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, double score, [MarshalAs(UnmanagedType.LPUTF8Str)] string member);

    [DllImport(LinuxLib, EntryPoint = "cache_zadd", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_zadd_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, double score, [MarshalAs(UnmanagedType.LPUTF8Str)] string member);

    [DllImport(MacLib, EntryPoint = "cache_zadd", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_zadd_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, double score, [MarshalAs(UnmanagedType.LPUTF8Str)] string member);


    [DllImport(WindowsLib, EntryPoint = "cache_zrange", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_zrange_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, int start, int end, out UIntPtr len);

    [DllImport(LinuxLib, EntryPoint = "cache_zrange", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_zrange_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, int start, int end, out UIntPtr len);

    [DllImport(MacLib, EntryPoint = "cache_zrange", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_zrange_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, int start, int end, out UIntPtr len);


    public static void ZAdd(string key, double score, string member)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(member);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            cache_zadd_win(key, score, member);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            cache_zadd_linux(key, score, member);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            cache_zadd_mac(key, score, member);
        else
            throw new PlatformNotSupportedException();
    }

    public static List<string> ZRange(string key, int start, int end)
    {
        UIntPtr len;
        IntPtr ptr;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            ptr = cache_zrange_win(key, start, end, out len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            ptr = cache_zrange_linux(key, start, end, out len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
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
