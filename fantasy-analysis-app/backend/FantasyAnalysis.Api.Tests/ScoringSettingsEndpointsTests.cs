using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FantasyAnalysis.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class ScoringSettingsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ScoringSettingsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetThenPut_RoundTripsSettings()
    {
        var client = _factory.CreateClient();
        var settings = new ScoringSettings(
            new List<ScoringCategory> { new("homeRuns", 4m) },
            new List<ScoringCategory> { new("strikeOuts", 1m) },
            new Dictionary<string, int> { ["C"] = 1 });

        var putResponse = await client.PutAsJsonAsync("/api/settings/scoring", settings);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var getResponse = await client.GetAsync("/api/settings/scoring");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var loaded = await getResponse.Content.ReadFromJsonAsync<ScoringSettings>();
        Assert.Equal(4m, loaded!.HittingCategories[0].PointsPerUnit);
    }
}
