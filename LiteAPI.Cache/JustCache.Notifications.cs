using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace LiteAPI.Cache;

public static partial class JustCache
{
    #region Phase3: Keyspace Notifications

    [DllImport(WindowsLib, EntryPoint = "cache_notifications_poll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_notifications_poll_win(out UIntPtr len);

    [DllImport(LinuxLib, EntryPoint = "cache_notifications_poll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_notifications_poll_linux(out UIntPtr len);

    [DllImport(MacLib, EntryPoint = "cache_notifications_poll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_notifications_poll_mac(out UIntPtr len);


    [DllImport(WindowsLib, EntryPoint = "cache_notifications_clear", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_notifications_clear_win();

    [DllImport(LinuxLib, EntryPoint = "cache_notifications_clear", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_notifications_clear_linux();

    [DllImport(MacLib, EntryPoint = "cache_notifications_clear", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_notifications_clear_mac();


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
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            cache_notifications_clear_win();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            cache_notifications_clear_linux();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            cache_notifications_clear_mac();
        else
            throw new PlatformNotSupportedException();
    }

    public static bool TryPollNotification(out KeyspaceNotification notification)
    {
        notification = default;

        UIntPtr len;
        IntPtr ptr;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            ptr = cache_notifications_poll_win(out len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            ptr = cache_notifications_poll_linux(out len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
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
