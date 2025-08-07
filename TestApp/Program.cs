using LiteAPI.Cache;

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