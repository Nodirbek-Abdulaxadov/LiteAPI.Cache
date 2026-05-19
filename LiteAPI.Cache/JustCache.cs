using System.Runtime.CompilerServices;
﻿using System.Runtime.InteropServices;

namespace LiteAPI.Cache;

public static partial class JustCache
{
    #region Library Loading

    private const string WindowsLib = "rust_cache.dll";
    private const string LinuxLib = "librust_cache.so";
    private const string MacLib = "librust_cache.dylib";

    // Resolved once at type init; replaces per-call `RuntimeInformation.IsOSPlatform`
    // branches across every native dispatch wrapper. Saves ~3 method calls + 3
    // string comparisons per cache operation.
    internal enum Platform { Windows, Linux, OSX }
    internal static readonly Platform _platform = DetectPlatform();

    private static Platform DetectPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return Platform.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))   return Platform.Linux;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))     return Platform.OSX;
        throw new PlatformNotSupportedException("Unknown OS");
    }

    private static string GetLibraryName() => _platform switch
    {
        Platform.Windows => WindowsLib,
        Platform.Linux   => LinuxLib,
        Platform.OSX     => MacLib,
        _ => throw new PlatformNotSupportedException("Unknown OS"),
    };

    static JustCache()
    {
        var libName = GetLibraryName();
        var baseDir = AppContext.BaseDirectory;

        // 1. NuGet native assets (preferred — RID-specific, signed-by-CI).
        var ridPath = Path.Combine(baseDir, "runtimes", GetRuntimeRid(), "native", libName);
        if (File.Exists(ridPath))
        {
            NativeLibrary.Load(ridPath);
            return;
        }

        // 2. App-local deployment (dev workflow: copy from RustLib/target/release).
        var directPath = Path.Combine(baseDir, libName);
        if (File.Exists(directPath))
        {
            NativeLibrary.Load(directPath);
            return;
        }

        // No OS-search-path fallback: prevents an attacker-placed
        // `rust_cache.dll` in System32 / LD_LIBRARY_PATH from being loaded
        // ahead of the bundled binary.
        throw new DllNotFoundException(
            $"Unable to load native library '{libName}'. Looked in: '{ridPath}' and '{directPath}'.");
    }

    private static string GetRuntimeRid()
    {
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64   => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException($"Unsupported architecture: {RuntimeInformation.OSArchitecture}")
        };

        return _platform switch
        {
            Platform.Windows => $"win-{arch}",
            Platform.Linux   => $"linux-{arch}",
            Platform.OSX     => $"osx-{arch}",
            _ => throw new PlatformNotSupportedException("Unknown OS"),
        };
    }

    #endregion

    #region Cache Initialization

    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_init", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_init_win();
#else
    [DllImport(WindowsLib, EntryPoint = "cache_init", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_init_win();
#endif
    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_init", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_init_linux();
#else
    [DllImport(LinuxLib, EntryPoint = "cache_init", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_init_linux();
#endif
    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_init", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_init_mac();
#else
    [DllImport(MacLib, EntryPoint = "cache_init", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_init_mac();
#endif

    public static void Initialize()
    {
        if (_platform == Platform.Windows)
            cache_init_win();
        else if (_platform == Platform.Linux)
            cache_init_linux();
        else if (_platform == Platform.OSX)
            cache_init_mac();
        else
            throw new PlatformNotSupportedException("Unknown OS");
    }
    
    #endregion

    #region  Set Method

    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_set", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_set_win(string key, byte[] val, UIntPtr len);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_set", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_set", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_set_linux(string key, byte[] val, UIntPtr len);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_set", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_set", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_set_mac(string key, byte[] val, UIntPtr len);
#else
    [DllImport(MacLib, EntryPoint = "cache_set", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_set_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte[] val, UIntPtr len);
#endif

    public static void Set(string key, byte[] val)
    {
        var len = (UIntPtr)val.Length;

        if (_platform == Platform.Windows)
            cache_set_win(key, val, len);
        else if (_platform == Platform.Linux)
            cache_set_linux(key, val, len);
        else if (_platform == Platform.OSX)
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

    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_get", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_get_win(string key, out UIntPtr len);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_get", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_get_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key, out UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_get", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_get_linux(string key, out UIntPtr len);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_get", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_get_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key, out UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_get", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_get_mac(string key, out UIntPtr len);
#else
    [DllImport(MacLib, EntryPoint = "cache_get", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_get_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key, out UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_free", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_free_win(IntPtr ptr, UIntPtr len);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_free_win(IntPtr ptr, UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_free", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_free_linux(IntPtr ptr, UIntPtr len);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_free_linux(IntPtr ptr, UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_free", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_free_mac(IntPtr ptr, UIntPtr len);
#else
    [DllImport(MacLib, EntryPoint = "cache_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_free_mac(IntPtr ptr, UIntPtr len);
#endif

    public static byte[]? Get(string key)
    {
        UIntPtr len;

        IntPtr ptr;
        if (_platform == Platform.Windows)
            ptr = cache_get_win(key, out len);
        else if (_platform == Platform.Linux)
            ptr = cache_get_linux(key, out len);
        else if (_platform == Platform.OSX)
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

            if (_platform == Platform.Windows)
                cache_free_win(ptr, len);
            else if (_platform == Platform.Linux)
                cache_free_linux(ptr, len);
            else if (_platform == Platform.OSX)
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
    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_remove", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_remove_win(string key);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_remove", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_remove_win([MarshalAs(UnmanagedType.LPUTF8Str)] string key);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_clear_all", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_clear_all_win();
#else
    [DllImport(WindowsLib, EntryPoint = "cache_clear_all", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_clear_all_win();
#endif

    // Linux
    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_remove", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_remove_linux(string key);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_remove", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_remove_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string key);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_clear_all", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_clear_all_linux();
#else
    [DllImport(LinuxLib, EntryPoint = "cache_clear_all", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_clear_all_linux();
#endif

    // macOS
    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_remove", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_remove_mac(string key);
#else
    [DllImport(MacLib, EntryPoint = "cache_remove", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_remove_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string key);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_clear_all", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_clear_all_mac();
#else
    [DllImport(MacLib, EntryPoint = "cache_clear_all", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_clear_all_mac();
#endif

    public static void Remove(string key)
    {
        if (_platform == Platform.Windows)
            cache_remove_win(key);
        else if (_platform == Platform.Linux)
            cache_remove_linux(key);
        else if (_platform == Platform.OSX)
            cache_remove_mac(key);

        JustCacheEventSource.Log.ReportRemove();
    }

    public static void ClearAll()
    {
        if (_platform == Platform.Windows)
            cache_clear_all_win();
        else if (_platform == Platform.Linux)
            cache_clear_all_linux();
        else if (_platform == Platform.OSX)
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