using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using FantasyKeeper.Api.Tests.Fakes;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class KeepersServiceTests
{
    private static (FakeConfigStore Config, FakeSheetsClient Sheets, KeepersService Service) Build(string seasonStatus = "active")
    {
        var config = new FakeConfigStore
        {
            Seasons = new List<Season> { new("2026", "2026 Season", "sheet-1", seasonStatus, DateTimeOffset.UtcNow) },
            Teams = new List<Team> { new("b-squared", "B Squared", "1111") },
            Mappings = new Dictionary<string, Dictionary<string, TeamMapping>>
            {
                ["2026"] = new()
                {
                    ["b-squared"] = new TeamMapping("2026 Keepers", "H8:H9", "C8:F9")
                }
            }
        };

        var sheets = new FakeSheetsClient();
        sheets.Seed("sheet-1", "2026 Keepers", "H8:H9", new List<IReadOnlyList<string>>
        {
            new List<string> { "T. Story" },
            new List<string> { "" }
        });
        sheets.Seed("sheet-1", "2026 Keepers", "C8:F9", new List<IReadOnlyList<string>>
        {
            new List<string> { "T. Story", "1", "14", "2" },
            new List<string> { "", "", "", "" }
        });

        return (config, sheets, new KeepersService(sheets, config));
    }

    [Fact]
    public async Task GetKeeperDataAsync_ReturnsParsedRows()
    {
        var (_, _, service) = Build();

        var data = await service.GetKeeperDataAsync("2026", "b-squared");

        Assert.Equal("B Squared", data.TeamName);
        Assert.False(data.ReadOnly);
        Assert.Equal("T. Story", data.NewContracts[0].Player);
        Assert.Equal(1, data.NewContracts[0].ContractType);
        Assert.Equal(14, data.NewContracts[0].Salary);
    }

    [Fact]
    public async Task GetKeeperDataAsync_ArchivedSeason_IsReadOnly()
    {
        var (_, _, service) = Build(seasonStatus: "archived");

        var data = await service.GetKeeperDataAsync("2026", "b-squared");

        Assert.True(data.ReadOnly);
    }

    [Fact]
    public async Task UpdateKeeperDataAsync_ValidSubmission_WritesRange()
    {
        var (_, sheets, service) = Build();
        var submission = new KeeperSubmission(new List<KeeperRow>
        {
            new("New Guy", 1, 10, 2),
            new("", null, null, null)
        });

        await service.UpdateKeeperDataAsync("2026", "b-squared", submission);

        var update = Assert.Single(sheets.Updates);
        Assert.Equal("C8:F9", update.Range);
        Assert.Equal("New Guy", update.Values[0][0]);
    }

    [Fact]
    public async Task UpdateKeeperDataAsync_InvalidContractType_Throws()
    {
        var (_, _, service) = Build();
        var submission = new KeeperSubmission(new List<KeeperRow>
        {
            new("New Guy", 3, 10, 2),
            new("", null, null, null)
        });

        await Assert.ThrowsAsync<KeeperValidationException>(
            () => service.UpdateKeeperDataAsync("2026", "b-squared", submission));
    }

    [Fact]
    public async Task UpdateKeeperDataAsync_WrongRowCount_Throws()
    {
        var (_, _, service) = Build();
        var submission = new KeeperSubmission(new List<KeeperRow> { new("New Guy", 1, 10, 2) });

        await Assert.ThrowsAsync<KeeperValidationException>(
            () => service.UpdateKeeperDataAsync("2026", "b-squared", submission));
    }

    [Fact]
    public async Task UpdateKeeperDataAsync_ArchivedSeason_Throws()
    {
        var (_, _, service) = Build(seasonStatus: "archived");
        var submission = new KeeperSubmission(new List<KeeperRow>
        {
            new("New Guy", 1, 10, 2),
            new("", null, null, null)
        });

        await Assert.ThrowsAsync<SeasonNotActiveException>(
            () => service.UpdateKeeperDataAsync("2026", "b-squared", submission));
    }

    [Theory]
    [InlineData("=ARRAYFORMULA(A1:A10)")]
    [InlineData("+1+1")]
    [InlineData("-1")]
    [InlineData("@SUM(A1)")]
    public async Task UpdateKeeperDataAsync_PlayerNameStartsWithFormulaChar_Throws(string playerName)
    {
        var (_, _, service) = Build();
        var submission = new KeeperSubmission(new List<KeeperRow>
        {
            new(playerName, 1, 10, 2),
            new("", null, null, null)
        });

        await Assert.ThrowsAsync<KeeperValidationException>(
            () => service.UpdateKeeperDataAsync("2026", "b-squared", submission));
    }

    [Fact]
    public async Task UpdateKeeperDataAsync_TwoTeams_OnlyWritesSubmittingTeamsRange()
    {
        var config = new FakeConfigStore
        {
            Seasons = new List<Season> { new("2026", "2026 Season", "sheet-1", "active", DateTimeOffset.UtcNow) },
            Teams = new List<Team>
            {
                new("b-squared", "B Squared", "1111"),
                new("other-team", "Other Team", "2222")
            },
            Mappings = new Dictionary<string, Dictionary<string, TeamMapping>>
            {
                ["2026"] = new()
                {
                    ["b-squared"] = new TeamMapping("2026 Keepers", "H8:H9", "C8:F9"),
                    ["other-team"] = new TeamMapping("2026 Keepers", "H20:H21", "C20:F21")
                }
            }
        };

        var sheets = new FakeSheetsClient();
        sheets.Seed("sheet-1", "2026 Keepers", "H8:H9", new List<IReadOnlyList<string>>
        {
            new List<string> { "T. Story" },
            new List<string> { "" }
        });
        sheets.Seed("sheet-1", "2026 Keepers", "C8:F9", new List<IReadOnlyList<string>>
        {
            new List<string> { "T. Story", "1", "14", "2" },
            new List<string> { "", "", "", "" }
        });

        var service = new KeepersService(sheets, config);

        var submission = new KeeperSubmission(new List<KeeperRow>
        {
            new("New Guy", 1, 10, 2),
            new("", null, null, null)
        });

        await service.UpdateKeeperDataAsync("2026", "b-squared", submission);

        var update = Assert.Single(sheets.Updates);
        Assert.Equal("C8:F9", update.Range);
        Assert.NotEqual("C20:F21", update.Range);
    }
}
