using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace LiteAPI.Cache;

public static partial class JustCache
{
    #region Phase3: Pub/Sub

    [DllImport(WindowsLib, EntryPoint = "cache_pubsub_subscribe", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong cache_pubsub_subscribe_win([MarshalAs(UnmanagedType.LPUTF8Str)] string channel);

    [DllImport(LinuxLib, EntryPoint = "cache_pubsub_subscribe", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong cache_pubsub_subscribe_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string channel);

    [DllImport(MacLib, EntryPoint = "cache_pubsub_subscribe", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong cache_pubsub_subscribe_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string channel);


    [DllImport(WindowsLib, EntryPoint = "cache_pubsub_unsubscribe", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_pubsub_unsubscribe_win(ulong subId);

    [DllImport(LinuxLib, EntryPoint = "cache_pubsub_unsubscribe", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_pubsub_unsubscribe_linux(ulong subId);

    [DllImport(MacLib, EntryPoint = "cache_pubsub_unsubscribe", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cache_pubsub_unsubscribe_mac(ulong subId);


    [DllImport(WindowsLib, EntryPoint = "cache_pubsub_publish", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong cache_pubsub_publish_win([MarshalAs(UnmanagedType.LPUTF8Str)] string channel, byte[] payload, UIntPtr len);

    [DllImport(LinuxLib, EntryPoint = "cache_pubsub_publish", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong cache_pubsub_publish_linux([MarshalAs(UnmanagedType.LPUTF8Str)] string channel, byte[] payload, UIntPtr len);

    [DllImport(MacLib, EntryPoint = "cache_pubsub_publish", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong cache_pubsub_publish_mac([MarshalAs(UnmanagedType.LPUTF8Str)] string channel, byte[] payload, UIntPtr len);


    [DllImport(WindowsLib, EntryPoint = "cache_pubsub_poll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_pubsub_poll_win(ulong subId, out UIntPtr len);

    [DllImport(LinuxLib, EntryPoint = "cache_pubsub_poll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_pubsub_poll_linux(ulong subId, out UIntPtr len);

    [DllImport(MacLib, EntryPoint = "cache_pubsub_poll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr cache_pubsub_poll_mac(ulong subId, out UIntPtr len);


    public readonly record struct PubSubMessage(string Channel, byte[] Payload)
    {
        public string PayloadAsString() => Encoding.UTF8.GetString(Payload);
    }

    public static ulong Subscribe(string channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return cache_pubsub_subscribe_win(channel);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return cache_pubsub_subscribe_linux(channel);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return cache_pubsub_subscribe_mac(channel);

        throw new PlatformNotSupportedException();
    }

    public static void Unsubscribe(ulong subId)
    {
        if (subId == 0) return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            cache_pubsub_unsubscribe_win(subId);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            cache_pubsub_unsubscribe_linux(subId);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            cache_pubsub_unsubscribe_mac(subId);
        else
            throw new PlatformNotSupportedException();
    }

    public static ulong Publish(string channel, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(payload);

        var len = (UIntPtr)payload.Length;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return cache_pubsub_publish_win(channel, payload, len);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return cache_pubsub_publish_linux(channel, payload, len);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return cache_pubsub_publish_mac(channel, payload, len);

        throw new PlatformNotSupportedException();
    }

    public static ulong PublishString(string channel, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Publish(channel, Encoding.UTF8.GetBytes(message));
    }

    public static bool TryPoll(ulong subId, out PubSubMessage message)
    {
        message = default;
        if (subId == 0) return false;

        UIntPtr len;
        IntPtr ptr;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            ptr = cache_pubsub_poll_win(subId, out len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            ptr = cache_pubsub_poll_linux(subId, out len);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            ptr = cache_pubsub_poll_mac(subId, out len);
        else
            throw new PlatformNotSupportedException();

        if (ptr == IntPtr.Zero || len == UIntPtr.Zero)
            return false;

        var blob = CopyAndFree(ptr, len);
        if (blob.Length < 8)
            return false;

        int offset = 0;
        uint chLen = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(offset, 4));
        offset += 4;
        if (offset + chLen + 4 > blob.Length)
            return false;

        string ch = Encoding.UTF8.GetString(blob, offset, (int)chLen);
        offset += (int)chLen;

        uint pLen = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(offset, 4));
        offset += 4;
        if (offset + pLen > blob.Length)
            return false;

        byte[] payload = new byte[pLen];
        Buffer.BlockCopy(blob, offset, payload, 0, (int)pLen);

        message = new PubSubMessage(ch, payload);
        return true;
    }

    #endregion
}
