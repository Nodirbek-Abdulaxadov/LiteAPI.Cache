using StackExchange.Redis;

namespace LiteAPI.Cache.Benchmarks;

internal sealed class RedisBenchClient : IDisposable
{
    public const int DefaultDatabase = 15;

    public ConnectionMultiplexer Multiplexer { get; }
    public IDatabase Db { get; }
    public IServer Server { get; }
    public int Database { get; }

    private RedisBenchClient(ConnectionMultiplexer multiplexer, IServer server, int database)
    {
        Multiplexer = multiplexer;
        Server = server;
        Database = database;
        Db = multiplexer.GetDatabase(database);
    }

    public static bool IsAvailable()
    {
        var force = Environment.GetEnvironmentVariable("FORCE_REDIS");
        if (string.Equals(force, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(force, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            using var client = ConnectOrThrow();
            _ = client.Db.Ping();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static RedisBenchClient ConnectOrThrow()
    {
        var connectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";

        var dbEnv = Environment.GetEnvironmentVariable("REDIS_DB");
        var db = DefaultDatabase;
        if (!string.IsNullOrWhiteSpace(dbEnv) && int.TryParse(dbEnv, out var parsed))
            db = parsed;

        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;

        var allowAdmin = Environment.GetEnvironmentVariable("REDIS_ALLOW_ADMIN");
        options.AllowAdmin = string.Equals(allowAdmin, "1", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(allowAdmin, "true", StringComparison.OrdinalIgnoreCase);

        options.ConnectTimeout = 5000;
        options.SyncTimeout = 5000;

        var mux = ConnectionMultiplexer.Connect(options);
        var endpoint = options.EndPoints.FirstOrDefault()
            ?? throw new InvalidOperationException("No redis endpoints found in REDIS_CONNECTION.");

        var server = mux.GetServer(endpoint);
        return new RedisBenchClient(mux, server, db);
    }

    public void ResetDatabase()
    {
        // Optional cleanup: only works if REDIS_ALLOW_ADMIN=true.
        try
        {
            if (Multiplexer.Configuration.Contains("allowAdmin=true", StringComparison.OrdinalIgnoreCase))
                Server.FlushDatabase(Database);
        }
        catch
        {
            // ignore
        }
    }

    public void Dispose()
    {
        Multiplexer.Dispose();
    }
}
