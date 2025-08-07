using LiteAPI.Cache;

public class TopupRam
{
    public static void TryTopupRam()
    {
        JustCache.Initialize();

        JustCache.SetString(GenerateRandomKey(), GenerateRandomValue(1024 * 1024 * 500));
        Console.WriteLine($"Current memory usage: {GetMemoryUsage() / (1024 * 1024)} MB");
    }

    public static string GenerateRandomKey()
    {
        return Guid.NewGuid().ToString();
    }

    public static string GenerateRandomValue(int size)
    {
        var randomBytes = new byte[size];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }
        return Convert.ToBase64String(randomBytes);
    }

    public static long GetMemoryUsage(bool f = false)
    {
        return GC.GetTotalMemory(f);
    }
}