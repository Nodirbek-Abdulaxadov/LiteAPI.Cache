using System.Security.Cryptography;

namespace LiteAPI.Cache.Benchmarks;

internal static class TestData
{
    public static byte[] CreatePayload(int bytes)
    {
        if (bytes < 0)
            throw new ArgumentOutOfRangeException(nameof(bytes));

        var payload = new byte[bytes];
        RandomNumberGenerator.Fill(payload);
        return payload;
    }
}
