using System;
using System.Collections.Generic;
using System.IO;
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class FileKeepersDataStoreTests : IDisposable
{
    private readonly string _tempDir;

    public FileKeepersDataStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void LoadData_WhenFileMissing_ReturnsNull()
    {
        var store = new FileKeepersDataStore(_tempDir);
        Assert.Null(store.LoadData());
    }

    [Fact]
    public void SaveAndLoadData_RoundTrips()
    {
        var store = new FileKeepersDataStore(_tempDir);
        var data = new KeepersData(
            "test.xlsx",
            "2026 Keepers",
            DateTimeOffset.UtcNow,
            new Dictionary<string, StoredTeamKeepers>
            {
                ["b-squared"] = new StoredTeamKeepers(
                    "B Squared",
                    7,
                    new List<int> { 8, 9 },
                    new List<KeeperRow> { new("T. Story", 1, 14, 2) },
                    new List<int> { 8 },
                    new List<ExistingContractRow> { new("J. Dominguez", "#1 - 2/3", 3, 1.34m, 1.34m) })
            });

        store.SaveData(data);
        var loaded = store.LoadData();

        Assert.NotNull(loaded);
        Assert.Equal("test.xlsx", loaded!.SourceFileName);
        Assert.Equal("T. Story", loaded.Teams["b-squared"].NewContracts[0].Player);
    }

    // Regression: data saved before ExistingContractsRows/ExistingContracts existed has no such
    // keys on disk, and System.Text.Json fills a missing IReadOnlyList<T> constructor parameter
    // with null despite the non-nullable annotation. LoadData must normalize those to empty.
    [Fact]
    public void LoadData_LegacyJsonMissingExistingContractFields_NormalizesToEmptyLists()
    {
        var legacyJson = """
        {
          "sourceFileName": "legacy.xlsx",
          "sheetName": "2026 Keepers",
          "lastUpdatedUtc": "2026-01-01T00:00:00+00:00",
          "teams": {
            "b-squared": {
              "rawNameInSheet": "B Squared",
              "headerRow": 7,
              "newContractsRows": [8],
              "newContracts": [{"player":"T. Story","contractType":1,"salary":14,"keeperYears":2}]
            }
          }
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "current-keepers.json"), legacyJson);

        var store = new FileKeepersDataStore(_tempDir);
        var loaded = store.LoadData();

        Assert.NotNull(loaded);
        var team = loaded!.Teams["b-squared"];
        Assert.NotNull(team.ExistingContractsRows);
        Assert.Empty(team.ExistingContractsRows);
        Assert.NotNull(team.ExistingContracts);
        Assert.Empty(team.ExistingContracts);

        // Fields that did exist before must survive normalization untouched.
        Assert.Equal("B Squared", team.RawNameInSheet);
        Assert.Equal(7, team.HeaderRow);
        Assert.Equal(new[] { 8 }, team.NewContractsRows);
        Assert.Equal("T. Story", Assert.Single(team.NewContracts).Player);
    }

    [Fact]
    public void LoadWorkbook_WhenFileMissing_ReturnsNull()
    {
        var store = new FileKeepersDataStore(_tempDir);
        Assert.Null(store.LoadWorkbook());
    }

    [Fact]
    public void SaveAndLoadWorkbook_RoundTrips()
    {
        var store = new FileKeepersDataStore(_tempDir);
        var bytes = new byte[] { 1, 2, 3, 4 };

        store.SaveWorkbook(bytes);
        var loaded = store.LoadWorkbook();

        Assert.Equal(bytes, loaded);
    }

    [Fact]
    public void SaveData_OverwritesExistingFileAndLeavesNoTempFileBehind()
    {
        var store = new FileKeepersDataStore(_tempDir);
        var first = new KeepersData("first.xlsx", "2026 Keepers", DateTimeOffset.UtcNow,
            new Dictionary<string, StoredTeamKeepers>());
        var second = new KeepersData("second.xlsx", "2026 Keepers", DateTimeOffset.UtcNow,
            new Dictionary<string, StoredTeamKeepers>());

        store.SaveData(first);
        store.SaveData(second);

        Assert.Equal("second.xlsx", store.LoadData()!.SourceFileName);
        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }

    [Fact]
    public void SaveWorkbook_OverwritesExistingFileAndLeavesNoTempFileBehind()
    {
        var store = new FileKeepersDataStore(_tempDir);

        store.SaveWorkbook(new byte[] { 1, 2, 3, 4 });
        store.SaveWorkbook(new byte[] { 9, 9 });

        Assert.Equal(new byte[] { 9, 9 }, store.LoadWorkbook());
        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }
}
