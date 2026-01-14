using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace LiteAPI.Cache;

public static partial class JustCache
{
    #region Hashes (HSET/HGET/HGETALL)

    [DllImport(WindowsLib, EntryPoint = "cache_hset", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_hset_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string field, byte[] val, UIntPtr len);

    [DllImport(LinuxLib, EntryPoint = "cache_hset", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_hset_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string field, byte[] val, UIntPtr len);

    [DllImport(MacLib, EntryPoint = "cache_hset", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_hset_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string field, byte[] val, UIntPtr len);


    [DllImport(WindowsLib, EntryPoint = "cache_hget", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_hget_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string field, out UIntPtr len);

    [DllImport(LinuxLib, EntryPoint = "cache_hget", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_hget_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string field, out UIntPtr len);

    [DllImport(MacLib, EntryPoint = "cache_hget", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_hget_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string field, out UIntPtr len);


    [DllImport(WindowsLib, EntryPoint = "cache_hgetall", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_hgetall_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, out UIntPtr len);

    [DllImport(LinuxLib, EntryPoint = "cache_hgetall", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_hgetall_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, out UIntPtr len);

    [DllImport(MacLib, EntryPoint = "cache_hgetall", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_hgetall_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, out UIntPtr len);


    public static void HSet(string key, string field, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);

        var len = (UIntPtr)value.Length;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            cache_hset_win(key, field, value, len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            cache_hset_linux(key, field, value, len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            cache_hset_mac(key, field, value, len);
        else
            throw new PlatformNotSupportedException();
    }

    public static void HSetString(string key, string field, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        HSet(key, field, Encoding.UTF8.GetBytes(value));
    }

    public static byte[]? HGet(string key, string field)
    {
        UIntPtr len;
        IntPtr ptr;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            ptr = cache_hget_win(key, field, out len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            ptr = cache_hget_linux(key, field, out len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            ptr = cache_hget_mac(key, field, out len);
        else
            throw new PlatformNotSupportedException();

        if (ptr == IntPtr.Zero || len == UIntPtr.Zero)
            return null;

        return CopyAndFree(ptr, len);
    }

    public static string? HGetString(string key, string field)
    {
        var bytes = HGet(key, field);
        return bytes == null ? null : Encoding.UTF8.GetString(bytes);
    }

    public static Dictionary<string, byte[]> HGetAll(string key)
    {
        UIntPtr len;
        IntPtr ptr;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            ptr = cache_hgetall_win(key, out len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            ptr = cache_hgetall_linux(key, out len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            ptr = cache_hgetall_mac(key, out len);
        else
            throw new PlatformNotSupportedException();

        if (ptr == IntPtr.Zero || len == UIntPtr.Zero)
            return new Dictionary<string, byte[]>(StringComparer.Ordinal);

        var blob = CopyAndFree(ptr, len);
        return ParseHGetAllBlob(blob);
    }

    private static Dictionary<string, byte[]> ParseHGetAllBlob(byte[] blob)
    {
        // format: [Count (u32)] [KeyLen (u32)] [Key] [ValLen (u32)] [Val] ...
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (blob.Length < 4)
            return result;

        int offset = 0;
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(offset, 4));
        offset += 4;

        for (uint i = 0; i < count; i++)
        {
            if (offset + 4 > blob.Length) break;
            uint keyLen = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(offset, 4));
            offset += 4;
            if (offset + keyLen > blob.Length) break;

            string field = Encoding.UTF8.GetString(blob, offset, (int)keyLen);
            offset += (int)keyLen;

            if (offset + 4 > blob.Length) break;
            uint valLen = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(offset, 4));
            offset += 4;
            if (offset + valLen > blob.Length) break;

            byte[] val = new byte[valLen];
            Buffer.BlockCopy(blob, offset, val, 0, (int)valLen);
            offset += (int)valLen;

            result[field] = val;
        }

        return result;
    }

    #endregion
}
