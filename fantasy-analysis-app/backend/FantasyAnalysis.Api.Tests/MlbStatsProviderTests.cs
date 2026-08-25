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

    [Fact]
    public async Task GetPlayerStatsAsync_FetchesHittingAndPitchingAndSkipsEmptyGroups()
    {
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            var url = req.RequestUri!.ToString();
            string json;
            if (url.Contains("group=hitting"))
            {
                json = """{ "stats": [ { "splits": [ { "stat": { "homeRuns": 44, "avg": ".310" } } ] } ] }""";
            }
            else
            {
                json = """{ "stats": [ { "splits": [] } ] }""";
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });
        var client = new HttpClient(handler) { BaseAddress = new System.Uri("https://statsapi.mlb.com/") };
        var provider = new MlbStatsProvider(client);

        var lines = await provider.GetPlayerStatsAsync(new[] { "660271" }, 2026);

        var line = Assert.Single(lines);
        Assert.Equal("hitting", line.Group);
        Assert.Equal(44m, line.Stats["homeRuns"]);
        Assert.Equal(0.310m, line.Stats["avg"]);
    }

    [Fact]
    public async Task GetPlayerStatsAsync_TwoWayPlayer_ReturnsBothGroups()
    {
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            var json = """{ "stats": [ { "splits": [ { "stat": { "strikeOuts": 200 } } ] } ] }""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        });
        var client = new HttpClient(handler) { BaseAddress = new System.Uri("https://statsapi.mlb.com/") };
        var provider = new MlbStatsProvider(client);

        var lines = await provider.GetPlayerStatsAsync(new[] { "660271" }, 2026);

        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.Group == "hitting");
        Assert.Contains(lines, l => l.Group == "pitching");
    }

    [Fact]
    public async Task GetPlayerStatsAsync_MissingStatsArray_ThrowsStatsProviderException()
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        });
        var client = new HttpClient(handler) { BaseAddress = new System.Uri("https://statsapi.mlb.com/") };
        var provider = new MlbStatsProvider(client);

        await Assert.ThrowsAsync<StatsProviderException>(() => provider.GetPlayerStatsAsync(new[] { "660271" }, 2026));
    }
}
