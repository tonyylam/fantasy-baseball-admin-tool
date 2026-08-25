using System;
using System.Collections.Generic;
using System.IO;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class FileLeagueDataStoreTests : IDisposable
{
    private readonly string _tempDir;

    public FileLeagueDataStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void LoadLeague_WhenFileMissing_ReturnsNull()
    {
        var store = new FileLeagueDataStore(_tempDir);
        Assert.Null(store.LoadLeague());
    }

    [Fact]
    public void SaveAndLoadLeague_RoundTrips()
    {
        var store = new FileLeagueDataStore(_tempDir);
        var league = new League(
            DateTimeOffset.UtcNow,
            new List<TeamRoster>
            {
                new("Rhino Wranglers", new List<RosteredPlayer>
                {
                    new("Shohei Ohtani", "660271", "Shohei Ohtani", "DH", false)
                })
            });

        store.SaveLeague(league);
        var loaded = store.LoadLeague();

        Assert.NotNull(loaded);
        Assert.Single(loaded!.Teams);
        Assert.Equal("Shohei Ohtani", loaded.Teams[0].Players[0].PlayerFullName);
    }

    [Fact]
    public void SaveLeague_OverwritesExistingFileAndLeavesNoTempFileBehind()
    {
        var store = new FileLeagueDataStore(_tempDir);
        var first = new League(DateTimeOffset.UtcNow, new List<TeamRoster>());
        var second = new League(DateTimeOffset.UtcNow, new List<TeamRoster>
        {
            new("Sea Dogs", new List<RosteredPlayer>())
        });

        store.SaveLeague(first);
        store.SaveLeague(second);

        Assert.Single(store.LoadLeague()!.Teams);
        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }
}
