using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;

namespace LiteAPI.Cache;

public static partial class JustCache
{
    #region Lists (LPUSH/RPOP/LRANGE)

    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_lpush", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_lpush_win(string key, byte[] val, UIntPtr len);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_lpush", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_lpush_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_lpush", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_lpush_linux(string key, byte[] val, UIntPtr len);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_lpush", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_lpush_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_lpush", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_lpush_mac(string key, byte[] val, UIntPtr len);
#else
    [DllImport(MacLib, EntryPoint = "cache_lpush", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_lpush_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len);
#endif


    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_rpop", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_rpop_win(string key, out UIntPtr len);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_rpop", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_rpop_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, out UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_rpop", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_rpop_linux(string key, out UIntPtr len);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_rpop", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_rpop_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, out UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_rpop", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_rpop_mac(string key, out UIntPtr len);
#else
    [DllImport(MacLib, EntryPoint = "cache_rpop", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_rpop_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, out UIntPtr len);
#endif


    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_lrange", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_lrange_win(string key, int start, int end, out UIntPtr len);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_lrange", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_lrange_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, int start, int end, out UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_lrange", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_lrange_linux(string key, int start, int end, out UIntPtr len);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_lrange", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_lrange_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, int start, int end, out UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_lrange", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_lrange_mac(string key, int start, int end, out UIntPtr len);
#else
    [DllImport(MacLib, EntryPoint = "cache_lrange", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_lrange_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, int start, int end, out UIntPtr len);
#endif


    public static void LPush(string key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        var len = (UIntPtr)value.Length;
        if (_platform == Platform.Windows)
            cache_lpush_win(key, value, len);
        else if (_platform == Platform.Linux)
            cache_lpush_linux(key, value, len);
        else if (_platform == Platform.OSX)
            cache_lpush_mac(key, value, len);
        else
            throw new PlatformNotSupportedException();
    }

    public static void LPushString(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        LPush(key, Encoding.UTF8.GetBytes(value));
    }

    public static byte[]? RPop(string key)
    {
        UIntPtr len;
        IntPtr ptr;

        if (_platform == Platform.Windows)
            ptr = cache_rpop_win(key, out len);
        else if (_platform == Platform.Linux)
            ptr = cache_rpop_linux(key, out len);
        else if (_platform == Platform.OSX)
            ptr = cache_rpop_mac(key, out len);
        else
            throw new PlatformNotSupportedException();

        if (ptr == IntPtr.Zero || len == UIntPtr.Zero)
            return null;

        return CopyAndFree(ptr, len);
    }

    public static string? RPopString(string key)
    {
        var bytes = RPop(key);
        return bytes == null ? null : Encoding.UTF8.GetString(bytes);
    }

    public static List<byte[]> LRange(string key, int start, int end)
    {
        UIntPtr len;
        IntPtr ptr;

        if (_platform == Platform.Windows)
            ptr = cache_lrange_win(key, start, end, out len);
        else if (_platform == Platform.Linux)
            ptr = cache_lrange_linux(key, start, end, out len);
        else if (_platform == Platform.OSX)
            ptr = cache_lrange_mac(key, start, end, out len);
        else
            throw new PlatformNotSupportedException();

        if (ptr == IntPtr.Zero || len == UIntPtr.Zero)
            return new List<byte[]>();

        var blob = CopyAndFree(ptr, len);
        return ParseListRangeBlob(blob);
    }

    public static List<string> LRangeStrings(string key, int start, int end)
    {
        var items = LRange(key, start, end);
        var result = new List<string>(items.Count);
        foreach (var item in items)
        {
            result.Add(Encoding.UTF8.GetString(item));
        }
        return result;
    }

    private static List<byte[]> ParseListRangeBlob(byte[] blob)
    {
        // format: [Count (u32)] [ItemLen (u32)] [Item] ...
        var result = new List<byte[]>();
        if (blob.Length < 4)
            return result;

        int offset = 0;
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(offset, 4));
        offset += 4;

        result.Capacity = (int)Math.Min(count, int.MaxValue);

        for (uint i = 0; i < count; i++)
        {
            if (offset + 4 > blob.Length) break;
            uint itemLen = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(offset, 4));
            offset += 4;
            if (offset + itemLen > blob.Length) break;

            byte[] item = new byte[itemLen];
            Buffer.BlockCopy(blob, offset, item, 0, (int)itemLen);
            offset += (int)itemLen;

            result.Add(item);
        }

        return result;
    }

    #endregion
}
