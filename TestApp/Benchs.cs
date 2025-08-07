using LiteAPI.Cache;

public class Benchs
{
    public static void RunBenchmarks()
    {

        List<(string title, string setElapsed, string getElapsed)> justCacheResults = [];
        List<(string title, string setElapsed, string getElapsed)> redisResults = [];
        List<(string title, string setElapsed, string getElapsed)> memoryCacheResults = [];
        List<(string title, string setElapsed, string getElapsed)> concurrentJustCacheResults = [];

        byte option = 0;
        while (true)
        {
            Console.WriteLine("Choose a cache to test:");
            Console.WriteLine("0. Topup RAM");
            Console.WriteLine("1. JustCache");
            Console.WriteLine("2. Redis");
            Console.WriteLine("3. MemoryCache");
            Console.WriteLine("4. Concurrent JustCache");
            Console.WriteLine("5. Exit");

            if (!byte.TryParse(Console.ReadLine(), out option) || option < 0 || option > 5)
            {
                Console.WriteLine("Invalid option, please try again.");
                continue;
            }

            if (option == 0)
            {
                while (true)
                {
                    TopupRam.TryTopupRam();
                    Console.WriteLine("Press any key to continue or 'q' to quit...");
                    var key = Console.ReadKey(true).Key;
                    if (key == ConsoleKey.Q)
                    {
                        JustCache.ClearAll();
                        Console.WriteLine("RAM topped up and cache cleared.");
                        Console.WriteLine($"Current memory usage: {TopupRam.GetMemoryUsage() / (1024 * 1024)} MB");
                        Console.WriteLine("Press any key to cleanup GC...");
                        Console.ReadKey();
                        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                        Console.WriteLine($"Memory usage after GC: {TopupRam.GetMemoryUsage(true) / (1024 * 1024)} MB");
                        Console.WriteLine("GC cleanup completed.");
                        Console.WriteLine("Exiting RAM top-up mode.");
                        Console.ReadKey();
                        break;
                    }
                }
            }

            if (option == 1)
            {
                justCacheResults = JustCacheTest.Start();
                Console.WriteLine("JustCache tests completed.");
            }
            else if (option == 2)
            {
                redisResults = RedisTest.Start();
                Console.WriteLine("Redis tests completed.");
            }
            else if (option == 3)
            {
                memoryCacheResults = MemoryTest.Start();
                Console.WriteLine("MemoryCache tests completed.");
            }
            else if (option == 4)
            {
                concurrentJustCacheResults = ConcurrencyTest.Start();
                Console.WriteLine("Concurrent JustCache tests completed.");
            }
            else if (option == 5)
            {
                Console.WriteLine("Exiting...");
                break;
            }
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
            Console.Clear();
        }

        Console.WriteLine("Performance Results:");
        // Assuming justCacheResults and redisResults are available

        Console.WriteLine("Title".PadRight(35) + "Set Elapsed (μs)".PadRight(25) + "Get Elapsed (μs)".PadRight(25));

        for (int i = 0; i < justCacheResults.Count; i++)
        {
            var redisResult = redisResults[i];
            // Display Redis results
            Console.WriteLine($"{redisResult.title.PadRight(35)}" +
                              $"{redisResult.setElapsed.PadRight(25)}" +
                              $"{redisResult.getElapsed.PadRight(25)}");

            var justCacheResult = justCacheResults[i];
            // Display JustCache results
            Console.WriteLine($"{justCacheResult.title.PadRight(35)}" +
                              $"{justCacheResult.setElapsed.PadRight(25)}" +
                              $"{justCacheResult.getElapsed.PadRight(25)}");

            var memoryResult = memoryCacheResults[i];
            // Display Memory results
            Console.WriteLine($"{memoryResult.title.PadRight(35)}" +
                              $"{memoryResult.setElapsed.PadRight(25)}" +
                              $"{memoryResult.getElapsed.PadRight(25)}");

            var concurrentResult = concurrentJustCacheResults[i];
            // Display Concurrent JustCache results
            Console.WriteLine($"{concurrentResult.title.PadRight(35)}" +
                              $"{concurrentResult.setElapsed.PadRight(25)}" +
                              $"{concurrentResult.getElapsed.PadRight(25)}");

            Console.WriteLine();
        }

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}