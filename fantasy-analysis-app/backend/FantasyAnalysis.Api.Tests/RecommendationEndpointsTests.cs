using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        var settings = new ScoringSettings(new List<string>(), new List<string>(), new Dictionary<string, int>());
        var responseJson = """
            {
                "waiverSuggestions": [
                    { "summary": "Pick up Jane Doe", "reasoning": "Better projected value than your current utility slot.", "involvedPlayerIds": ["jane-doe"], "citations": [] }
                ],
                "tradeSuggestions": []
            }
            """;

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

        // Read the raw HTTP response body (not through the file store) to verify that
        // RecommendationType is actually serialized as a string ("Waiver") on the wire,
        // not the enum's underlying int (0). A test that only round-trips through
        // ReadFromJsonAsync<RecommendationSet> with default options would pass either way
        // once JsonStringEnumConverter is registered, so we assert on the raw text too.
        var rawJson = await getResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"type\":\"Waiver\"", rawJson);

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var set = JsonSerializer.Deserialize<RecommendationSet>(rawJson, jsonOptions);
        Assert.NotNull(set);
        var waiverSuggestion = Assert.Single(set!.WaiverSuggestions);
        Assert.Equal(RecommendationType.Waiver, waiverSuggestion.Type);
    }

    [Fact]
    public async Task Refresh_WhenAnUnexpectedExceptionOccurs_ReturnsServerErrorWithMessageInsteadOfAnOpaqueResponse()
    {
        // Simulates a bug or corrupted data reaching a code path with no specific catch clause
        // (e.g. a category key that slipped past PUT validation) - the endpoint should still
        // surface a message rather than returning an empty/opaque 500, since that's the only
        // thing the frontend has to show the user.
        var league = new League(
            System.DateTimeOffset.UtcNow,
            new List<TeamRoster> { new("Rhino Wranglers", new List<RosteredPlayer>()) });
        var leagueStore = new FakeLeagueDataStore();
        leagueStore.SaveLeague(league);
        var settingsWithUnrecognizedCategory = new ScoringSettings(
            new List<string> { "notARealCategory" },
            new List<string>(),
            new Dictionary<string, int>());

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ILeagueDataStore>(leagueStore);
                services.AddSingleton<IScoringSettingsStore>(new FakeScoringSettingsStore(settingsWithUnrecognizedCategory));
                services.AddSingleton<IStatsProvider>(new FakeStatsProvider(new List<MlbPlayer>()));
                services.AddSingleton<IStatsCache>(new FakeStatsCache());
                services.AddSingleton<IRecommendationDataStore>(new FakeRecommendationDataStore());
            });
        });
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/recommendations/refresh?teamName=Rhino+Wranglers", null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("error", out var errorProp));
        Assert.False(string.IsNullOrWhiteSpace(errorProp.GetString()));
    }
}
