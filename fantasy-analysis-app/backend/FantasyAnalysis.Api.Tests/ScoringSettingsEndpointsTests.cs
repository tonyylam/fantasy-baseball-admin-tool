using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FantasyAnalysis.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class ScoringSettingsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ScoringSettingsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["AnthropicApiKey"] = "test-key" });
            });
        });
    }

    [Fact]
    public async Task GetThenPut_RoundTripsSettings()
    {
        var client = _factory.CreateClient();
        var settings = new ScoringSettings(
            new List<string> { "homeRuns" },
            new List<string> { "strikeoutsPer9Inn" },
            new Dictionary<string, int> { ["C"] = 1 });

        var putResponse = await client.PutAsJsonAsync("/api/settings/scoring", settings);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var getResponse = await client.GetAsync("/api/settings/scoring");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var loaded = await getResponse.Content.ReadFromJsonAsync<ScoringSettings>();
        Assert.Equal(new[] { "homeRuns" }, loaded!.HittingCategoryKeys);
    }

    [Fact]
    public async Task Put_WithUnrecognizedHittingCategoryKey_ReturnsBadRequestAndDoesNotPersist()
    {
        var client = _factory.CreateClient();
        var validSettings = new ScoringSettings(
            new List<string> { "homeRuns" },
            new List<string>(),
            new Dictionary<string, int> { ["C"] = 1 });
        var invalidSettings = new ScoringSettings(
            new List<string> { "notARealCategory" },
            new List<string>(),
            new Dictionary<string, int> { ["C"] = 1 });

        // Establish a known-good persisted state first, so this test's "did not persist"
        // assertion holds regardless of what earlier tests sharing this factory did.
        var seedResponse = await client.PutAsJsonAsync("/api/settings/scoring", validSettings);
        Assert.Equal(HttpStatusCode.OK, seedResponse.StatusCode);

        var putResponse = await client.PutAsJsonAsync("/api/settings/scoring", invalidSettings);
        Assert.Equal(HttpStatusCode.BadRequest, putResponse.StatusCode);

        var getResponse = await client.GetAsync("/api/settings/scoring");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var loaded = await getResponse.Content.ReadFromJsonAsync<ScoringSettings>();
        Assert.Equal(new[] { "homeRuns" }, loaded!.HittingCategoryKeys);
    }

    [Fact]
    public async Task Put_WithPitchingCategoryKeyPlacedInHittingList_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var settings = new ScoringSettings(
            new List<string> { "era" },
            new List<string>(),
            new Dictionary<string, int> { ["C"] = 1 });

        var putResponse = await client.PutAsJsonAsync("/api/settings/scoring", settings);
        Assert.Equal(HttpStatusCode.BadRequest, putResponse.StatusCode);
    }

    [Fact]
    public async Task GetAvailableCategories_ReturnsAllElevenKnownCategoriesWithGroups()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/settings/scoring/categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var categories = await response.Content.ReadFromJsonAsync<List<ScoringCategoryOptionDto>>();
        Assert.Equal(11, categories!.Count);
        Assert.Contains(categories, c => c.StatKey == "era" && c.Group == "pitching");
        Assert.Contains(categories, c => c.StatKey == "obp" && c.Group == "hitting");
    }

    private record ScoringCategoryOptionDto(string StatKey, string DisplayName, string Group);
}
