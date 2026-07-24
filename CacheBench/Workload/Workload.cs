namespace CacheBench.Workload;

// The shared "expensive factory" every cache calls on a miss. It counts its
// own invocations (so we can measure cache-stampede protection) and can
// simulate latency (so concurrent misses actually overlap).
public sealed class Factory
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);
    public void ResetCalls() => Interlocked.Exchange(ref _calls, 0);

    public int ValueBytes { get; }
    public int DelayMs { get; }

    private readonly string _payload;

    public Factory(int valueBytes = 256, int delayMs = 0)
    {
        ValueBytes = valueBytes;
        DelayMs = delayMs;
        // Deterministic, fixed-size value so every cache stores identical data.
        _payload = new string('x', Math.Max(1, valueBytes));
    }

    // Async so a DelayMs > 0 makes concurrent misses pile up (stampede test).
    public async ValueTask<string> ComputeAsync(string key)
    {
        Interlocked.Increment(ref _calls);
        if (DelayMs > 0)
            await Task.Delay(DelayMs).ConfigureAwait(false);
        return _payload;
    }

    public string Compute(string key)
    {
        Interlocked.Increment(ref _calls);
        return _payload;
    }
}

public static class Keys
{
    // Deterministic keys "k:000000".."k:NNNNNN" over a fixed working set.
    public static string[] Generate(int count)
    {
        var keys = new string[count];
        for (int i = 0; i < count; i++)
            keys[i] = "k:" + i.ToString("D6");
        return keys;
    }
}
