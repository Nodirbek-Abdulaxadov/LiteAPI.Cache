using System.Text.Json;

namespace LiteAPI.Cache;

public static partial class JustCache
{
    public static void SetObject<T>(string key, T items) where T : class
    {
        var json = JsonSerializer.Serialize(items);
        SetString(key, json);
    }

    public static T? GetObject<T>(string key) where T : class
    {
        var cachedJson = GetString(key);
        return cachedJson != null ? JsonSerializer.Deserialize<T>(cachedJson) : null;
    }

    public static void SetObjects<T>(string key, IEnumerable<T> items) where T : class
    {
        var json = JsonSerializer.Serialize(items);
        SetString(key, json);
    }

    public static IEnumerable<T>? GetObjects<T>(string key) where T : class
    {
        var cachedJson = GetString(key);
        return cachedJson != null ? JsonSerializer.Deserialize<IEnumerable<T>>(cachedJson) : null;
    }
}