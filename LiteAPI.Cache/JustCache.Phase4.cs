using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;

namespace LiteAPI.Cache;

public static partial class JustCache
{
    #region Phase4: JSON Path

    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_json_get", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_json_get_win(string key, string path, out UIntPtr len);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_json_get", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_json_get_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, out UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_json_get", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_json_get_linux(string key, string path, out UIntPtr len);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_json_get", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_json_get_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, out UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_json_get", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_json_get_mac(string key, string path, out UIntPtr len);
#else
    [DllImport(MacLib, EntryPoint = "cache_json_get", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_json_get_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, out UIntPtr len);
#endif

    public static byte[]? JsonGet(string key, string path)
    {
        UIntPtr len;

        IntPtr ptr;
        if (_platform == Platform.Windows)
            ptr = cache_json_get_win(key, path, out len);
        else if (_platform == Platform.Linux)
            ptr = cache_json_get_linux(key, path, out len);
        else if (_platform == Platform.OSX)
            ptr = cache_json_get_mac(key, path, out len);
        else
            throw new PlatformNotSupportedException();

        if (ptr == IntPtr.Zero || len == UIntPtr.Zero)
            return null;

        return CopyAndFree(ptr, len);
    }

    public static string? JsonGetString(string key, string path)
    {
        var bytes = JsonGet(key, path);
        return bytes == null ? null : Encoding.UTF8.GetString(bytes);
    }

    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_json_set", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int cache_json_set_win(string key, string path, byte[] jsonValue, UIntPtr len);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_json_set", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_json_set_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, byte[] jsonValue, UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_json_set", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int cache_json_set_linux(string key, string path, byte[] jsonValue, UIntPtr len);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_json_set", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_json_set_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, byte[] jsonValue, UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_json_set", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int cache_json_set_mac(string key, string path, byte[] jsonValue, UIntPtr len);
#else
    [DllImport(MacLib, EntryPoint = "cache_json_set", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_json_set_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, byte[] jsonValue, UIntPtr len);
#endif

    public static bool JsonSet(string key, string path, string jsonValue)
    {
        ArgumentNullException.ThrowIfNull(jsonValue);

        var bytes = Encoding.UTF8.GetBytes(jsonValue);
        var len = (UIntPtr)bytes.Length;

        int rc;
        if (_platform == Platform.Windows)
            rc = cache_json_set_win(key, path, bytes, len);
        else if (_platform == Platform.Linux)
            rc = cache_json_set_linux(key, path, bytes, len);
        else if (_platform == Platform.OSX)
            rc = cache_json_set_mac(key, path, bytes, len);
        else
            throw new PlatformNotSupportedException();

        return rc != 0;
    }

    #endregion

    #region Phase4: Secondary Index + Find

    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_index_create_numeric", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int cache_index_create_numeric_win(string field);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_index_create_numeric", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_index_create_numeric_win([MarshalAs(UnmanagedType.LPUTF8Str)] string field);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_index_create_numeric", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int cache_index_create_numeric_linux(string field);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_index_create_numeric", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_index_create_numeric_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string field);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_index_create_numeric", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int cache_index_create_numeric_mac(string field);
#else
    [DllImport(MacLib, EntryPoint = "cache_index_create_numeric", CallingConvention = CallingConvention.Cdecl)]
    private static extern int cache_index_create_numeric_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string field);
#endif

    public static bool CreateNumericIndex(string field)
    {
        int rc;
        if (_platform == Platform.Windows)
            rc = cache_index_create_numeric_win(field);
        else if (_platform == Platform.Linux)
            rc = cache_index_create_numeric_linux(field);
        else if (_platform == Platform.OSX)
            rc = cache_index_create_numeric_mac(field);
        else
            throw new PlatformNotSupportedException();

        return rc != 0;
    }

    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_find", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_find_win(string query, out UIntPtr len);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_find", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_find_win([MarshalAs(UnmanagedType.LPUTF8Str)] string query, out UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_find", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_find_linux(string query, out UIntPtr len);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_find", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_find_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string query, out UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_find", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_find_mac(string query, out UIntPtr len);
#else
    [DllImport(MacLib, EntryPoint = "cache_find", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_find_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string query, out UIntPtr len);
#endif

    public static IReadOnlyList<string> FindKeys(string query)
    {
        UIntPtr len;

        IntPtr ptr;
        if (_platform == Platform.Windows)
            ptr = cache_find_win(query, out len);
        else if (_platform == Platform.Linux)
            ptr = cache_find_linux(query, out len);
        else if (_platform == Platform.OSX)
            ptr = cache_find_mac(query, out len);
        else
            throw new PlatformNotSupportedException();

        if (ptr == IntPtr.Zero || len == UIntPtr.Zero)
            return Array.Empty<string>();

        var bytes = CopyAndFree(ptr, len);
        return DecodeKeyList(bytes);
    }

    private static IReadOnlyList<string> DecodeKeyList(byte[] bytes)
    {
        if (bytes.Length < 4)
            return Array.Empty<string>();

        int offset = 0;
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
        offset += 4;

        var keys = new List<string>((int)Math.Min(count, 4096));
        for (uint i = 0; i < count; i++)
        {
            if (offset + 4 > bytes.Length)
                break;

            uint keyLen = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
            offset += 4;

            if (keyLen > int.MaxValue || offset + (int)keyLen > bytes.Length)
                break;

            var key = Encoding.UTF8.GetString(bytes, offset, (int)keyLen);
            offset += (int)keyLen;

            keys.Add(key);
        }

        return keys;
    }

    #endregion

    #region Phase4: Eval

    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_eval", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_eval_win(string script, out UIntPtr len);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_eval", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_eval_win([MarshalAs(UnmanagedType.LPUTF8Str)] string script, out UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_eval", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_eval_linux(string script, out UIntPtr len);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_eval", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_eval_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string script, out UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_eval", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_eval_mac(string script, out UIntPtr len);
#else
    [DllImport(MacLib, EntryPoint = "cache_eval", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_eval_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string script, out UIntPtr len);
#endif

    public static byte[]? Eval(string script)
    {
        UIntPtr len;

        IntPtr ptr;
        if (_platform == Platform.Windows)
            ptr = cache_eval_win(script, out len);
        else if (_platform == Platform.Linux)
            ptr = cache_eval_linux(script, out len);
        else if (_platform == Platform.OSX)
            ptr = cache_eval_mac(script, out len);
        else
            throw new PlatformNotSupportedException();

        if (ptr == IntPtr.Zero || len == UIntPtr.Zero)
            return null;

        return CopyAndFree(ptr, len);
    }

    public static string? EvalString(string script)
    {
        var bytes = Eval(script);
        return bytes == null ? null : Encoding.UTF8.GetString(bytes);
    }

    #endregion
}
