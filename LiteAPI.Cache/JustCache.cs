using System.Runtime.InteropServices;

namespace LiteAPI.Cache;

public static partial class JustCache
{
    #region Library Loading

    private const string WindowsLib = "rust_cache.dll";
    private const string LinuxLib = "librust_cache.so";
    private const string MacLib = "librust_cache.dylib";

    private static string GetLibraryName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WindowsLib;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return LinuxLib;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return MacLib;

        throw new PlatformNotSupportedException("Unknown OS");
    }

    static JustCache()
    {
        // Yuklash uchun to‘liq yo‘lni o‘rnatish (ixtiyoriy)
        var libPath = Path.Combine(AppContext.BaseDirectory, GetLibraryName());
        NativeLibrary.Load(libPath);
    }

    #endregion

    #region Cache Initialization

    [DllImport(WindowsLib, EntryPoint = "cache_init", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_init_win();
    [DllImport(LinuxLib, EntryPoint = "cache_init", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_init_linux();
    [DllImport(MacLib, EntryPoint = "cache_init", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_init_mac();

    public static void Initialize()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            cache_init_win();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            cache_init_linux();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            cache_init_mac();
        else
            throw new PlatformNotSupportedException("Unknown OS");
    }
    
    #endregion

    #region  Set Method

    [DllImport(WindowsLib, EntryPoint = "cache_set", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_win(string key, byte[] val, UIntPtr len);

    [DllImport(LinuxLib, EntryPoint = "cache_set", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_linux(string key, byte[] val, UIntPtr len);

    [DllImport(MacLib, EntryPoint = "cache_set", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_mac(string key, byte[] val, UIntPtr len);

    public static void Set(string key, byte[] val)
    {
        var len = (UIntPtr)val.Length;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            cache_set_win(key, val, len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            cache_set_linux(key, val, len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            cache_set_mac(key, val, len);
    }

    public static void SetString(string key, string val)
    {
        ArgumentNullException.ThrowIfNull(val);

        // Convert string to byte array
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(val);
        Set(key, bytes);
    }

    #endregion

    #region  Get Method

    [DllImport(WindowsLib, EntryPoint = "cache_get", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_get_win(string key, out UIntPtr len);

    [DllImport(LinuxLib, EntryPoint = "cache_get", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_get_linux(string key, out UIntPtr len);

    [DllImport(MacLib, EntryPoint = "cache_get", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_get_mac(string key, out UIntPtr len);

    public static byte[]? Get(string key)
    {
        UIntPtr len;

        IntPtr ptr;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            ptr = cache_get_win(key, out len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            ptr = cache_get_linux(key, out len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            ptr = cache_get_mac(key, out len);
        else
            throw new PlatformNotSupportedException();

        if (ptr == IntPtr.Zero || len == UIntPtr.Zero)
            return null;

        byte[] result = new byte[(int)len];
        Marshal.Copy(ptr, result, 0, (int)len);
        return result;
    }

    public static string? GetString(string key)
    {
        byte[]? bytes = Get(key);
        if (bytes == null)
            return null;

        // Convert byte array to string
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    #endregion

    #region  Clear Methods

    // Windows
    [DllImport("rust_cache.dll", EntryPoint = "cache_remove", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_remove_win(string key);

    [DllImport("rust_cache.dll", EntryPoint = "cache_clear_all", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_clear_all_win();

    // Linux
    [DllImport("librust_cache.so", EntryPoint = "cache_remove", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_remove_linux(string key);

    [DllImport("librust_cache.so", EntryPoint = "cache_clear_all", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_clear_all_linux();

    // macOS
    [DllImport("librust_cache.dylib", EntryPoint = "cache_remove", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_remove_mac(string key);

    [DllImport("librust_cache.dylib", EntryPoint = "cache_clear_all", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_clear_all_mac();

    public static void Remove(string key)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            cache_remove_win(key);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            cache_remove_linux(key);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            cache_remove_mac(key);
    }

    public static void ClearAll()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            cache_clear_all_win();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            cache_clear_all_linux();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            cache_clear_all_mac();
    }

    #endregion
}