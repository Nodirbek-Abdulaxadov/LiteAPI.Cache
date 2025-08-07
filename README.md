# LiteAPI.Cache - JustCache

**GC-free, cross-platform in-memory cache for .NET backed by Rust.**

JustCache is a high-performance memory cache system built to bypass .NET's garbage collector by leveraging native Rust memory management. Designed for low-latency, high-throughput scenarios where predictability and performance are essential.

---

## 🚀 Key Features

- ⚡ **GC-Free**: No garbage collection pressure in .NET
- 🧠 **Native performance** using Rust under the hood
- 💼 **Cross-platform**: Supports Windows, Linux, and macOS
- 🔒 **Thread-safe** read/write access
- 💾 **Supports** strings, byte arrays, and JSON-serializable objects
- 🧩 **Interop via NativeAOT or P/Invoke**
- 🛡️ **Safe memory management** without leaks

---

## 📦 Installation

Install the NuGet package:

```bash
dotnet add package LiteAPI.Cache
```


> 🔧 Requires a precompiled native Rust dynamic library. See the documentation or [GitHub repository](https://github.com/Nodirbek-Abdulaxadov/LiteAPI.Cache) for details.

---

## ⚙️ Usage

- **Initialize the cache** at application startup
- **Set/Get** data by key (supports string, bytes, and object types)
- **Remove** individual keys or **clear all**
- **Interop with Rust** is handled internally—no manual marshaling needed

```csharp
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
```

---

## 🧠 Why JustCache?

- 🚀 Ultra-fast native cache access
- ✅ No impact on .NET GC or memory fragmentation
- 🧩 Drop-in utility for microservices, real-time systems, or edge apps
- 🔍 Useful for caching config, lookup tables, auth sessions, and more

---

## 🪪 License

MIT License © 2025 LiteAPI

---

## 💬 Feedback

Found a bug or want a feature? Open an issue or PR on [GitHub](https://github.com/Nodirbek-Abdulaxadov/LiteAPI.Cache).


## 🛠️ Contributing

We welcome contributions! Please see the [CONTRIBUTING.md](https://github.com/Nodirbek-Abdulaxadov/LiteAPI.Cache/CONTRIBUTING.md) for guidelines.