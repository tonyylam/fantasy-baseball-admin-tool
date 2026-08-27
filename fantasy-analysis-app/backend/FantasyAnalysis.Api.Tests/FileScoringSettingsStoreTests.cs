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
    public void Load_WhenFileIsPreMigrationShape_ReturnsNullInsteadOfBrokenObject()
    {
        // Exact shape of a real pre-migration scoring-settings.json saved by a user before
        // ScoringSettings moved from hittingCategories/pitchingCategories (with statKey/pointsPerUnit)
        // to HittingCategoryKeys/PitchingCategoryKeys. Deserializing this into the new record leaves
        // both key lists null rather than throwing, so Load() must detect and reject it explicitly.
        const string preMigrationJson = """
        {
          "hittingCategories": [
            { "statKey": "Run", "pointsPerUnit": 1 },
            { "statKey": "RBI", "pointsPerUnit": 1 },
            { "statKey": "SLG", "pointsPerUnit": 1 },
            { "statKey": "OBP", "pointsPerUnit": 1 },
            { "statKey": "HR", "pointsPerUnit": 1 },
            { "statKey": "SB", "pointsPerUnit": 1 }
          ],
          "pitchingCategories": [
            { "statKey": "Wins", "pointsPerUnit": 1 },
            { "statKey": "Saves", "pointsPerUnit": 1 },
            { "statKey": "ERA", "pointsPerUnit": 1 },
            { "statKey": "K/9", "pointsPerUnit": 1 },
            { "statKey": "WHIP", "pointsPerUnit": 1 },
            { "statKey": "Quality Starts", "pointsPerUnit": 1 }
          ],
          "rosterSlots": {
            "21": 6
          }
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "scoring-settings.json"), preMigrationJson);
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
