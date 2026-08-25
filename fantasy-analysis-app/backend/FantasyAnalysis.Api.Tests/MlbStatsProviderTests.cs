using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using FantasyAnalysis.Api.Tests.Fakes;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class MlbStatsProviderTests
{
    [Fact]
    public async Task GetAllActivePlayersAsync_ParsesPlayerFields()
    {
        var json = """
        {
          "people": [
            {
              "id": 660271,
              "fullName": "Shohei Ohtani",
              "active": true,
              "primaryPosition": { "code": "10", "name": "Designated Hitter", "type": "Hitter", "abbreviation": "DH" },
              "currentTeam": { "id": 119 }
            },
            {
              "id": 605483,
              "fullName": "Gerrit Cole",
              "active": true,
              "primaryPosition": { "code": "1", "name": "Pitcher", "type": "Pitcher", "abbreviation": "P" },
              "currentTeam": { "id": 147 }
            }
          ]
        }
        """;
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            Assert.Contains("/api/v1/sports/1/players", req.RequestUri!.ToString());
            Assert.Contains("season=2026", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });
        var client = new HttpClient(handler) { BaseAddress = new System.Uri("https://statsapi.mlb.com/") };
        var provider = new MlbStatsProvider(client);

        var players = await provider.GetAllActivePlayersAsync(2026);

        Assert.Equal(2, players.Count);
        Assert.Equal("660271", players[0].Id);
        Assert.Equal("Shohei Ohtani", players[0].FullName);
        Assert.False(players[0].IsPitcher);
        Assert.Equal(119, players[0].MlbTeamId);
        Assert.True(players[1].IsPitcher);
    }

    [Fact]
    public async Task GetAllActivePlayersAsync_UnexpectedResponseShape_ThrowsStatsProviderException()
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"notPeople\": []}", System.Text.Encoding.UTF8, "application/json")
        });
        var client = new HttpClient(handler) { BaseAddress = new System.Uri("https://statsapi.mlb.com/") };
        var provider = new MlbStatsProvider(client);

        await Assert.ThrowsAsync<StatsProviderException>(() => provider.GetAllActivePlayersAsync(2026));
    }
}
