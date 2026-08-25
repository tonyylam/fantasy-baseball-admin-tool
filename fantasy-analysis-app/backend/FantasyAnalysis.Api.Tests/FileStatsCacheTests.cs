using System;
using System.Collections.Generic;
using System.IO;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class FileStatsCacheTests : IDisposable
{
    private readonly string _tempDir;

    public FileStatsCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void GetIfFresh_WhenNoCacheExists_ReturnsNull()
    {
        var cache = new FileStatsCache(_tempDir);
        Assert.Null(cache.GetIfFresh(2026, TimeSpan.FromHours(24)));
    }

    [Fact]
    public void StoreThenGetIfFresh_WithinMaxAge_ReturnsStatLines()
    {
        var cache = new FileStatsCache(_tempDir);
        var statLines = new List<StatLine>
        {
            new("660271", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 44m })
        };

        cache.Store(2026, statLines);
        var result = cache.GetIfFresh(2026, TimeSpan.FromHours(24));

        Assert.NotNull(result);
        Assert.Equal(44m, result![0].Stats["homeRuns"]);
    }

    [Fact]
    public void GetIfFresh_WhenCacheOlderThanMaxAge_ReturnsNull()
    {
        var oldEntryJson = """{ "fetchedAtUtc": "2000-01-01T00:00:00+00:00", "statLines": [] }""";
        File.WriteAllText(Path.Combine(_tempDir, "stats-cache-2026.json"), oldEntryJson);
        var cache = new FileStatsCache(_tempDir);

        var result = cache.GetIfFresh(2026, TimeSpan.FromHours(24));

        Assert.Null(result);
    }
}
