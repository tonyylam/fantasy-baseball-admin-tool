using System;
using System.Collections.Generic;
using System.IO;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class FileScoringSettingsStoreTests : IDisposable
{
    private readonly string _tempDir;

    public FileScoringSettingsStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Load_WhenFileMissing_ReturnsNull()
    {
        var store = new FileScoringSettingsStore(_tempDir);
        Assert.Null(store.Load());
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var store = new FileScoringSettingsStore(_tempDir);
        var settings = new ScoringSettings(
            new List<string> { "homeRuns", "stolenBases" },
            new List<string> { "strikeoutsPer9Inn" },
            new Dictionary<string, int> { ["C"] = 1, ["1B"] = 1, ["SP"] = 5 });

        store.Save(settings);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(new[] { "homeRuns", "stolenBases" }, loaded!.HittingCategoryKeys);
        Assert.Equal(5, loaded.RosterSlots["SP"]);
    }
}
