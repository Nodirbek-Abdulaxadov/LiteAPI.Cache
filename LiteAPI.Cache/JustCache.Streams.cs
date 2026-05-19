using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace LiteAPI.Cache;

public static partial class JustCache
{
    #region Phase3: Streams (XADD/XRANGE)

    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_xadd", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial ulong cache_xadd_win(string key, byte[] payload, UIntPtr len);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_xadd", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong cache_xadd_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] payload, UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_xadd", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial ulong cache_xadd_linux(string key, byte[] payload, UIntPtr len);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_xadd", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong cache_xadd_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] payload, UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_xadd", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial ulong cache_xadd_mac(string key, byte[] payload, UIntPtr len);
#else
    [DllImport(MacLib, EntryPoint = "cache_xadd", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong cache_xadd_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] payload, UIntPtr len);
#endif


    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_xrange", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_xrange_win(string key, ulong startId, ulong endId, out UIntPtr len);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_xrange", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_xrange_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, ulong startId, ulong endId, out UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_xrange", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_xrange_linux(string key, ulong startId, ulong endId, out UIntPtr len);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_xrange", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_xrange_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, ulong startId, ulong endId, out UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_xrange", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_xrange_mac(string key, ulong startId, ulong endId, out UIntPtr len);
#else
    [DllImport(MacLib, EntryPoint = "cache_xrange", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_xrange_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, ulong startId, ulong endId, out UIntPtr len);
#endif


    public readonly record struct StreamItem(ulong Id, byte[] Payload);

    public static ulong XAdd(string key, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(payload);

        var len = (UIntPtr)payload.Length;

        if (_platform == Platform.Windows)
            return cache_xadd_win(key, payload, len);
        if (_platform == Platform.Linux)
            return cache_xadd_linux(key, payload, len);
        if (_platform == Platform.OSX)
            return cache_xadd_mac(key, payload, len);

        throw new PlatformNotSupportedException();
    }

    public static List<StreamItem> XRange(string key, ulong startId, ulong endId)
    {
        UIntPtr len;
        IntPtr ptr;

        if (_platform == Platform.Windows)
            ptr = cache_xrange_win(key, startId, endId, out len);
        else if (_platform == Platform.Linux)
            ptr = cache_xrange_linux(key, startId, endId, out len);
        else if (_platform == Platform.OSX)
            ptr = cache_xrange_mac(key, startId, endId, out len);
        else
            throw new PlatformNotSupportedException();

        if (ptr == IntPtr.Zero || len == UIntPtr.Zero)
            return new List<StreamItem>();

        var blob = CopyAndFree(ptr, len);
        return ParseXRangeBlob(blob);
    }

    private static List<StreamItem> ParseXRangeBlob(byte[] blob)
    {
        // format: [Count (u32)] [Id (u64)] [PayloadLen (u32)] [Payload] ...
        var result = new List<StreamItem>();
        if (blob.Length < 4)
            return result;

        int offset = 0;
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(offset, 4));
        offset += 4;

        result.Capacity = (int)Math.Min(count, int.MaxValue);

        for (uint i = 0; i < count; i++)
        {
            if (offset + 8 + 4 > blob.Length) break;

            ulong id = BinaryPrimitives.ReadUInt64LittleEndian(blob.AsSpan(offset, 8));
            offset += 8;

            uint plen = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(offset, 4));
            offset += 4;

            if (offset + plen > blob.Length) break;

            byte[] payload = new byte[plen];
            Buffer.BlockCopy(blob, offset, payload, 0, (int)plen);
            offset += (int)plen;

            result.Add(new StreamItem(id, payload));
        }

        return result;
    }

    #endregion
}
