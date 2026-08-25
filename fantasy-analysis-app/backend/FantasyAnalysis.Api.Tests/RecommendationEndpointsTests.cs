using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using FantasyAnalysis.Api.Tests.Fakes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class RecommendationEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RecommendationEndpointsTests(WebApplicationFactory<Program> factory)
    {
        var league = new League(
            System.DateTimeOffset.UtcNow,
            new List<TeamRoster> { new("Rhino Wranglers", new List<RosteredPlayer>()) });
        var leagueStore = new FakeLeagueDataStore();
        leagueStore.SaveLeague(league);
        var settings = new ScoringSettings(new List<ScoringCategory>(), new List<ScoringCategory>(), new Dictionary<string, int>());
        var responseJson = """{ "waiverSuggestions": [], "tradeSuggestions": [] }""";

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["AnthropicApiKey"] = "test-key" });
            });
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ILeagueDataStore>(leagueStore);
                services.AddSingleton<IScoringSettingsStore>(new FakeScoringSettingsStore(settings));
                services.AddSingleton<IStatsProvider>(new FakeStatsProvider(new List<MlbPlayer>()));
                services.AddSingleton<IStatsCache>(new FakeStatsCache());
                services.AddSingleton<IRecommendationClient>(new FakeRecommendationClient(responseJson));
                // Faked (not in the original brief snippet) so this test is deterministic across
                // repeated `dotnet test` runs: the real FileRecommendationDataStore persists
                // recommendations.json to disk under bin output, which would make
                // GetRecommendations_WhenNoneGenerated_ReturnsNotFound fail on any run after the
                // first — the same reason LeagueEndpointsTests fakes ILeagueDataStore.
                services.AddSingleton<IRecommendationDataStore>(new FakeRecommendationDataStore());
            });
        });
    }

    [Fact]
    public async Task GetRecommendations_WhenNoneGenerated_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/recommendations");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_ThenGet_ReturnsSavedRecommendations()
    {
        var client = _factory.CreateClient();

        var refreshResponse = await client.PostAsync("/api/recommendations/refresh?teamName=Rhino+Wranglers", null);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        var getResponse = await client.GetAsync("/api/recommendations");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var set = await getResponse.Content.ReadFromJsonAsync<RecommendationSet>();
        Assert.NotNull(set);
    }
}
