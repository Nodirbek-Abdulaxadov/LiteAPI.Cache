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
        var libName = GetLibraryName();
        var baseDir = AppContext.BaseDirectory;

        // Prefer app-local deployment (e.g. TestApp output).
        var directPath = Path.Combine(baseDir, libName);
        if (File.Exists(directPath))
        {
            NativeLibrary.Load(directPath);
            return;
        }

        // NuGet native assets are usually under runtimes/<rid>/native/.
        var ridPath = Path.Combine(baseDir, "runtimes", GetRuntimeRid(), "native", libName);
        if (File.Exists(ridPath))
        {
            NativeLibrary.Load(ridPath);
            return;
        }

        // Last resort: rely on OS loader search paths.
        if (NativeLibrary.TryLoad(libName, out _))
            return;

        throw new DllNotFoundException(
            $"Unable to load native library '{libName}'. Looked in: '{directPath}' and '{ridPath}'.");
    }

    private static string GetRuntimeRid()
    {
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException($"Unsupported architecture: {RuntimeInformation.OSArchitecture}")
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return $"win-{arch}";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return $"linux-{arch}";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return $"osx-{arch}";

        throw new PlatformNotSupportedException("Unknown OS");
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
    private static extern void cache_set_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len);

    [DllImport(LinuxLib, EntryPoint = "cache_set", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len);

    [DllImport(MacLib, EntryPoint = "cache_set", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len);

    public static void Set(string key, byte[] val)
    {
        var len = (UIntPtr)val.Length;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            cache_set_win(key, val, len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            cache_set_linux(key, val, len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            cache_set_mac(key, val, len);

        JustCacheEventSource.Log.ReportSet(val.Length);
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
    private static extern IntPtr cache_get_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, out UIntPtr len);

    [DllImport(LinuxLib, EntryPoint = "cache_get", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_get_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, out UIntPtr len);

    [DllImport(MacLib, EntryPoint = "cache_get", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_get_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, out UIntPtr len);

    [DllImport(WindowsLib, EntryPoint = "cache_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_free_win(IntPtr ptr, UIntPtr len);

    [DllImport(LinuxLib, EntryPoint = "cache_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_free_linux(IntPtr ptr, UIntPtr len);

    [DllImport(MacLib, EntryPoint = "cache_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_free_mac(IntPtr ptr, UIntPtr len);

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
        {
            JustCacheEventSource.Log.ReportGetMiss();
            return null;
        }

        byte[] result = new byte[(int)len];
        Marshal.Copy(ptr, result, 0, (int)len);
        FreeNative(ptr, len);

        JustCacheEventSource.Log.ReportGetHit(result.Length);
        return result;
    }

    private static void FreeNative(IntPtr ptr, UIntPtr len)
    {
        // Free the native buffer if cache_free exists (fallback for older DLLs)
        try
        {
            if (ptr == IntPtr.Zero || len == UIntPtr.Zero)
                return;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                cache_free_win(ptr, len);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                cache_free_linux(ptr, len);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                cache_free_mac(ptr, len);
        }
        catch (EntryPointNotFoundException) { }
        catch (DllNotFoundException) { }
    }

    private static byte[] CopyAndFree(IntPtr ptr, UIntPtr len)
    {
        if (ptr == IntPtr.Zero || len == UIntPtr.Zero)
            return Array.Empty<byte>();

        byte[] result = new byte[(int)len];
        Marshal.Copy(ptr, result, 0, (int)len);
        FreeNative(ptr, len);
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

    public static bool TryGet(string key, out byte[] value)
    {
        var bytes = Get(key);
        if (bytes == null)
        {
            value = Array.Empty<byte>();
            return false;
        }
        value = bytes;
        return true;
    }

    public static bool TryGetString(string key, out string value)
    {
        var s = GetString(key);
        if (s == null)
        {
            value = string.Empty;
            return false;
        }
        value = s;
        return true;
    }

    #endregion

    #region  Clear Methods

    // Windows
    [DllImport("rust_cache.dll", EntryPoint = "cache_remove", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_remove_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key);

    [DllImport("rust_cache.dll", EntryPoint = "cache_clear_all", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_clear_all_win();

    // Linux
    [DllImport("librust_cache.so", EntryPoint = "cache_remove", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_remove_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key);

    [DllImport("librust_cache.so", EntryPoint = "cache_clear_all", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_clear_all_linux();

    // macOS
    [DllImport("librust_cache.dylib", EntryPoint = "cache_remove", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_remove_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key);

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

        JustCacheEventSource.Log.ReportRemove();
    }

    public static void ClearAll()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            cache_clear_all_win();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            cache_clear_all_linux();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            cache_clear_all_mac();

        JustCacheEventSource.Log.ReportClear();
    }

    #endregion
}

internal sealed class JustCacheEventSource : System.Diagnostics.Tracing.EventSource
{
    public static readonly JustCacheEventSource Log = new JustCacheEventSource();

    private System.Diagnostics.Tracing.EventCounter? _setCounter;
    private System.Diagnostics.Tracing.EventCounter? _getHitCounter;
    private System.Diagnostics.Tracing.EventCounter? _getMissCounter;
    private System.Diagnostics.Tracing.EventCounter? _removeCounter;
    private System.Diagnostics.Tracing.EventCounter? _clearCounter;

    private JustCacheEventSource() : base("LiteAPI.JustCache")
    {
        _setCounter = new System.Diagnostics.Tracing.EventCounter("justcache-set", this);
        _getHitCounter = new System.Diagnostics.Tracing.EventCounter("justcache-get-hit", this);
        _getMissCounter = new System.Diagnostics.Tracing.EventCounter("justcache-get-miss", this);
        _removeCounter = new System.Diagnostics.Tracing.EventCounter("justcache-remove", this);
        _clearCounter = new System.Diagnostics.Tracing.EventCounter("justcache-clear", this);
    }

    public void ReportSet(int bytes) => _setCounter?.WriteMetric(bytes);
    public void ReportGetHit(int bytes) => _getHitCounter?.WriteMetric(bytes);
    public void ReportGetMiss() => _getMissCounter?.WriteMetric(1);
    public void ReportRemove() => _removeCounter?.WriteMetric(1);
    public void ReportClear() => _clearCounter?.WriteMetric(1);

    protected override void Dispose(bool disposing)
    {
        _setCounter?.Dispose();
        _getHitCounter?.Dispose();
        _getMissCounter?.Dispose();
        _removeCounter?.Dispose();
        _clearCounter?.Dispose();
        base.Dispose(disposing);
    }
}