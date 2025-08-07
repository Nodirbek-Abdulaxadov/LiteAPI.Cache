using System.Diagnostics;

public class Student
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }

    public static Student Random(int id)
    {
        var random = new Random();
        return new Student
        {
            Id = id,
            Name = $"Student {id}",
            Age = random.Next(18, 30)
        };
    }

    public override string ToString()
    {
        return $"Id: {Id}, Name: {Name}, Age: {Age}";
    }
}

public static class StopwatchExtensions
{
    // Stopwatch'ni mikrosekundlarga aylantirish
    public static string ElapsedMicroseconds(this Stopwatch stopwatch)
    {
        // ElapsedTicks -> mikrosekundlarga aylantirish
        return (stopwatch.ElapsedTicks / (Stopwatch.Frequency / 1000000)).ToString("N0");
    }
}