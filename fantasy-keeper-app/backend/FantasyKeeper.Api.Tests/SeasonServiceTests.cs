using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using FantasyKeeper.Api.Tests.Fakes;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class SeasonServiceTests
{
    private static (FakeConfigStore Config, FakeDriveClient Drive, SeasonService Service) Build()
    {
        var config = new FakeConfigStore
        {
            Seasons = new List<Season> { new("2026", "2026 Season", "sheet-1", "active", DateTimeOffset.UtcNow) },
            Mappings = new Dictionary<string, Dictionary<string, TeamMapping>>
            {
                ["2026"] = new() { ["b-squared"] = new TeamMapping("2026 Keepers", "H8:H9", "C8:F9") }
            }
        };
        var drive = new FakeDriveClient { NextCopyId = "sheet-2027" };
        return (config, drive, new SeasonService(config, drive, "commissioner@example.com"));
    }

    [Fact]
    public async Task CreateNewSeasonAsync_CopiesActiveSheetAndShares()
    {
        var (_, drive, service) = Build();

        await service.CreateNewSeasonAsync("2027 Season");

        var copy = Assert.Single(drive.Copies);
        Assert.Equal("sheet-1", copy.FileId);
        Assert.Equal("2027 Season", copy.NewTitle);

        var share = Assert.Single(drive.Shares);
        Assert.Equal("sheet-2027", share.FileId);
        Assert.Equal("commissioner@example.com", share.Email);
    }

    [Fact]
    public async Task CreateNewSeasonAsync_ArchivesOldSeasonAndActivatesNew()
    {
        var (config, _, service) = Build();

        var newSeason = await service.CreateNewSeasonAsync("2027 Season");

        var seasons = config.GetSeasons();
        Assert.Equal(2, seasons.Count);
        Assert.Equal("archived", seasons.Single(s => s.Id == "2026").Status);
        Assert.True(newSeason.IsActive);
        Assert.Equal("sheet-2027", newSeason.GoogleSheetId);
    }

    [Fact]
    public async Task CreateNewSeasonAsync_ClonesTeamMappings()
    {
        var (config, _, service) = Build();

        var newSeason = await service.CreateNewSeasonAsync("2027 Season");

        var mappings = config.GetTeamMappings(newSeason.Id);
        Assert.True(mappings.ContainsKey("b-squared"));
        Assert.Equal("C8:F9", mappings["b-squared"].NewContractsRange);
    }
}
