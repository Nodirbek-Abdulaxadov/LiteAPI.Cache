using System.Text;
using LiteAPI.Cache;

public static class Phase3Verify
{
    public static void Run()
    {
        Console.WriteLine("Phase3 verify: start");
        JustCache.Initialize();
        JustCache.ClearAll();
        JustCache.ClearNotifications();

        VerifyPubSub();
        VerifyKeyspaceNotifications();
        VerifyStreams();

        Console.WriteLine("Phase3 verify: OK");
    }

    private static void VerifyPubSub()
    {
        var sub = JustCache.Subscribe("ch1");
        if (sub == 0)
            throw new Exception("Subscribe failed");

        var delivered = JustCache.PublishString("ch1", "hello");
        if (delivered < 1)
            throw new Exception("Publish failed: expected delivered>=1");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 1000)
        {
            if (JustCache.TryPoll(sub, out var msg))
            {
                if (msg.Channel != "ch1" || msg.PayloadAsString() != "hello")
                    throw new Exception($"PubSub message mismatch: {msg.Channel} {msg.PayloadAsString()}");
                break;
            }
            Thread.Sleep(10);
        }

        if (sw.ElapsedMilliseconds >= 1000)
            throw new Exception("PubSub poll timed out");

        JustCache.Unsubscribe(sub);
    }

    private static void VerifyKeyspaceNotifications()
    {
        JustCache.ClearAll();
        JustCache.ClearNotifications();

        // Eviction notification
        JustCache.SetMaxItems(2);
        JustCache.SetString("n:k1", "1");
        JustCache.SetString("n:k2", "2");
        _ = JustCache.GetString("n:k1"); // make k2 the LRU
        JustCache.SetString("n:k3", "3"); // should evict k2

        bool sawEvicted = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 1000)
        {
            if (!JustCache.TryPollNotification(out var n))
            {
                Thread.Sleep(10);
                continue;
            }

            if (n.Kind == JustCache.NotificationKind.Evicted && n.Key == "n:k2")
            {
                sawEvicted = true;
                break;
            }
        }
        if (!sawEvicted)
            throw new Exception("Expected eviction notification for n:k2");

        // Expiry notification
        JustCache.ClearNotifications();
        JustCache.SetMaxItems(1000);
        JustCache.SetStringWithTtl("n:ttl", "x", TimeSpan.FromMilliseconds(150));

        bool sawExpired = false;
        sw.Restart();
        while (sw.ElapsedMilliseconds < 2000)
        {
            if (!JustCache.TryPollNotification(out var n))
            {
                Thread.Sleep(10);
                continue;
            }

            if (n.Kind == JustCache.NotificationKind.Expired && n.Key == "n:ttl")
            {
                sawExpired = true;
                break;
            }
        }
        if (!sawExpired)
            throw new Exception("Expected expiry notification for n:ttl");
    }

    private static void VerifyStreams()
    {
        JustCache.ClearAll();

        ulong id1 = JustCache.XAdd("s:orders", Encoding.UTF8.GetBytes("a"));
        ulong id2 = JustCache.XAdd("s:orders", Encoding.UTF8.GetBytes("b"));
        if (id1 == 0 || id2 == 0 || id2 <= id1)
            throw new Exception("XADD ids invalid");

        var items = JustCache.XRange("s:orders", id1, id2);
        if (items.Count != 2)
            throw new Exception($"XRANGE expected 2 items, got {items.Count}");

        if (Encoding.UTF8.GetString(items[0].Payload) != "a" || Encoding.UTF8.GetString(items[1].Payload) != "b")
            throw new Exception("XRANGE payload mismatch");
    }
}
