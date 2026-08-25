using System.Collections.Generic;
using System.Threading.Tasks;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using FantasyAnalysis.Api.Tests.Fakes;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class LeagueImportServiceTests
{
    private static readonly List<MlbPlayer> Pool = new()
    {
        new MlbPlayer("660271", "Shohei Ohtani", "DH", false, 119)
    };

    [Fact]
    public async Task PreviewImportAsync_ReturnsMatchPreviewPerTeam()
    {
        var service = new LeagueImportService(
            new RosterCsvParser(),
            new PlayerMatchingService(),
            new FakeStatsProvider(Pool),
            new FakeLeagueDataStore());

        var preview = await service.PreviewImportAsync("Team,Player\nRhino Wranglers,Shohei Ohtani\n");

        var team = Assert.Single(preview.Teams);
        Assert.Equal("Rhino Wranglers", team.TeamName);
        var player = Assert.Single(team.Players);
        Assert.Equal("660271", player.BestGuess!.PlayerId);
    }

    [Fact]
    public void ConfirmImport_DropsUnresolvedPlayersAndPersistsLeague()
    {
        var store = new FakeLeagueDataStore();
        var service = new LeagueImportService(
            new RosterCsvParser(),
            new PlayerMatchingService(),
            new FakeStatsProvider(Pool),
            store);
        var request = new ConfirmImportRequest(new List<ConfirmedTeam>
        {
            new("Rhino Wranglers", new List<ConfirmedPlayer>
            {
                new("Shohei Ohtani", "660271", "Shohei Ohtani", "DH", false),
                new("Unknown Guy", null, null, null, false)
            })
        });

        var league = service.ConfirmImport(request);

        var team = Assert.Single(league.Teams);
        var rostered = Assert.Single(team.Players);
        Assert.Equal("Shohei Ohtani", rostered.PlayerFullName);
        Assert.NotNull(store.Saved);
    }
}
