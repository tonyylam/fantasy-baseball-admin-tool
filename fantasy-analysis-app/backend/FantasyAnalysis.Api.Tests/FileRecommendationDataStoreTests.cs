using System;
using System.Collections.Generic;
using System.IO;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class FileRecommendationDataStoreTests : IDisposable
{
    private readonly string _tempDir;

    public FileRecommendationDataStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_WhenFileMissing_ReturnsNull()
    {
        var store = new FileRecommendationDataStore(_tempDir);
        Assert.Null(store.Load());
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var store = new FileRecommendationDataStore(_tempDir);
        var set = new RecommendationSet(
            DateTimeOffset.UtcNow,
            new List<Recommendation> { new(RecommendationType.Waiver, "Pick up X", "reason", new List<string> { "1" }, new List<string>(), 1) },
            new List<Recommendation>());

        store.Save(set);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal("Pick up X", loaded!.WaiverSuggestions[0].Summary);
    }
}
