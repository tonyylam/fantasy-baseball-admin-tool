using System;
using System.Collections.Generic;
using System.IO;
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class JsonConfigStoreTests : IDisposable
{
    private readonly string _tempDir;

    public JsonConfigStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void SaveAndGetSeasons_RoundTrips()
    {
        var store = new JsonConfigStore(_tempDir);
        var seasons = new List<Season> { new("2026", "2026 Season", "sheet-1", "active", DateTimeOffset.UtcNow) };

        store.SaveSeasons(seasons);
        var loaded = store.GetSeasons();

        Assert.Single(loaded);
        Assert.Equal("2026 Season", loaded[0].Label);
        Assert.True(loaded[0].IsActive);
    }

    [Fact]
    public void GetSeasons_WhenFileMissing_ReturnsEmptyList()
    {
        var store = new JsonConfigStore(_tempDir);
        Assert.Empty(store.GetSeasons());
    }

    [Fact]
    public void GetTeams_ReadsSeedFile()
    {
        File.WriteAllText(Path.Combine(_tempDir, "teams.json"),
            """[{"teamId":"b-squared","name":"B Squared","pin":"1111"}]""");

        var store = new JsonConfigStore(_tempDir);
        var teams = store.GetTeams();

        Assert.Single(teams);
        Assert.Equal("B Squared", teams[0].Name);
    }

    [Fact]
    public void SaveAndGetTeamMappings_RoundTrips()
    {
        var store = new JsonConfigStore(_tempDir);
        var mappings = new Dictionary<string, TeamMapping>
        {
            ["b-squared"] = new TeamMapping("2026 Keepers", "H8:N13", "C8:F13")
        };

        store.SaveTeamMappings("2026", mappings);
        var loaded = store.GetTeamMappings("2026");

        Assert.True(loaded.ContainsKey("b-squared"));
        Assert.Equal("C8:F13", loaded["b-squared"].NewContractsRange);
    }
}
