using System;
using System.IO;
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
    public void GetTeams_WhenFileMissing_ReturnsEmptyList()
    {
        var store = new JsonConfigStore(_tempDir);
        Assert.Empty(store.GetTeams());
    }
}
