using LiteAPI.Cache;

JustCache.Initialize();

JustCache.SetString("hello", "world");
Console.WriteLine($"hello -> {JustCache.GetString("hello")}");

JustCache.SetStringWithTtl("temp", "value", TimeSpan.FromMilliseconds(300));
Console.WriteLine($"temp ttl (ms) -> {JustCache.TtlMs("temp")}");

JustCache.Remove("hello");
Console.WriteLine($"hello removed -> {JustCache.GetString("hello") ?? "<null>"}");

JustCache.ClearAll();
Console.WriteLine("done");