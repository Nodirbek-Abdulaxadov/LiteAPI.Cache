using LiteAPI.Cache;

if (args.Length > 0 && string.Equals(args[0], "phase1", StringComparison.OrdinalIgnoreCase))
{
    Phase1Verify.Run();
    return;
}

if (args.Length > 0 && string.Equals(args[0], "phase2", StringComparison.OrdinalIgnoreCase))
{
	Phase2Verify.Run();
	return;
}

if (args.Length > 0 && string.Equals(args[0], "bench", StringComparison.OrdinalIgnoreCase))
{
	Benchs.RunBenchmarks();
	return;
}

if (args.Length > 0 && string.Equals(args[0], "concurrency", StringComparison.OrdinalIgnoreCase))
{
	var results = ConcurrencyTest.Start();
	foreach (var (title, setElapsed, getElapsed) in results)
	{
		Console.WriteLine($"{title} | set:{setElapsed} get:{getElapsed}");
	}
	return;
}

string key = "example_key";
Student student = Student.Random(1);

// Initialize the cache and perform operations
JustCache.Initialize();

// Set an object in the cache
JustCache.SetObject(key, student);

// Retrieve the object from the cache
student = JustCache.GetObject<Student>(key) ?? Student.Random(2);

// Display the retrieved object
Console.WriteLine(student);

// Remove the object from the cache
JustCache.Remove(key);

// Clear all cached objects
JustCache.ClearAll();