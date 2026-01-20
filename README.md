# LiteAPI.Cache (JustCache)

Fast, GC-free, cross-platform in-memory cache for .NET backed by Rust.

## Install

```bash
dotnet add package LiteAPI.Cache
```

> Requires a precompiled native Rust library. Build RustLib or use packaged runtimes.

## Quick start

```csharp
using LiteAPI.Cache;

JustCache.Initialize();

JustCache.SetString("hello", "world");
Console.WriteLine(JustCache.GetString("hello"));

JustCache.SetStringWithTtl("temp", "value", TimeSpan.FromSeconds(1));
Console.WriteLine(JustCache.TtlMs("temp"));

JustCache.Remove("hello");
JustCache.ClearAll();
```

## Notes

- Native library is required at runtime.
- Full documentation will live in a separate docs project.

## License

MIT License © 2025 LiteAPI