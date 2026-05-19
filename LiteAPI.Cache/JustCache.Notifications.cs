using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;

namespace LiteAPI.Cache;

public static partial class JustCache
{
    #region Phase3: Keyspace Notifications

    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_notifications_poll", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_notifications_poll_win(out UIntPtr len);
#else
    [DllImport(WindowsLib, EntryPoint = "cache_notifications_poll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_notifications_poll_win(out UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_notifications_poll", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_notifications_poll_linux(out UIntPtr len);
#else
    [DllImport(LinuxLib, EntryPoint = "cache_notifications_poll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_notifications_poll_linux(out UIntPtr len);
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_notifications_poll", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial IntPtr cache_notifications_poll_mac(out UIntPtr len);
#else
    [DllImport(MacLib, EntryPoint = "cache_notifications_poll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_notifications_poll_mac(out UIntPtr len);
#endif


    #if NET7_0_OR_GREATER
    [LibraryImport(WindowsLib, EntryPoint = "cache_notifications_clear", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_notifications_clear_win();
#else
    [DllImport(WindowsLib, EntryPoint = "cache_notifications_clear", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_notifications_clear_win();
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(LinuxLib, EntryPoint = "cache_notifications_clear", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_notifications_clear_linux();
#else
    [DllImport(LinuxLib, EntryPoint = "cache_notifications_clear", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_notifications_clear_linux();
#endif

    #if NET7_0_OR_GREATER
    [LibraryImport(MacLib, EntryPoint = "cache_notifications_clear", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void cache_notifications_clear_mac();
#else
    [DllImport(MacLib, EntryPoint = "cache_notifications_clear", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_notifications_clear_mac();
#endif


    public enum NotificationKind : byte
    {
        Expired = 1,
        Evicted = 2,
    }

    public readonly record struct KeyspaceNotification(NotificationKind Kind, string Key, ulong AtMs)
    {
        public override string ToString() => $"{Kind} {Key} @{AtMs}ms";
    }

    public static void ClearNotifications()
    {
        if (_platform == Platform.Windows)
            cache_notifications_clear_win();
        else if (_platform == Platform.Linux)
            cache_notifications_clear_linux();
        else if (_platform == Platform.OSX)
            cache_notifications_clear_mac();
        else
            throw new PlatformNotSupportedException();
    }

    public static bool TryPollNotification(out KeyspaceNotification notification)
    {
        notification = default;

        UIntPtr len;
        IntPtr ptr;

        if (_platform == Platform.Windows)
            ptr = cache_notifications_poll_win(out len);
        else if (_platform == Platform.Linux)
            ptr = cache_notifications_poll_linux(out len);
        else if (_platform == Platform.OSX)
            ptr = cache_notifications_poll_mac(out len);
        else
            throw new PlatformNotSupportedException();

        if (ptr == IntPtr.Zero || len == UIntPtr.Zero)
            return false;

        var blob = CopyAndFree(ptr, len);
        if (blob.Length < 1 + 4 + 8)
            return false;

        int offset = 0;
        var kind = (NotificationKind)blob[offset];
        offset += 1;

        uint klen = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(offset, 4));
        offset += 4;
        if (offset + klen + 8 > blob.Length)
            return false;

        string key = Encoding.UTF8.GetString(blob, offset, (int)klen);
        offset += (int)klen;

        ulong atMs = BinaryPrimitives.ReadUInt64LittleEndian(blob.AsSpan(offset, 8));

        notification = new KeyspaceNotification(kind, key, atMs);
        return true;
    }

    #endregion
}
