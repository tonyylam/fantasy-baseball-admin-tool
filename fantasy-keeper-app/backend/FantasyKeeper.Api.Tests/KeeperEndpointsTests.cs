using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class KeeperEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    // The server serializes responses with a camelCase naming policy
    // (Task 9's ConfigureHttpJsonOptions). HttpContent.ReadFromJsonAsync<T>()
    // defaults to case-sensitive matching when no options are passed, which
    // would silently leave PascalCase record properties unset — so every
    // response read below passes this explicit options instance.
    private static readonly JsonSerializerOptions ResponseJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _configRoot;

    public KeeperEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _configRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(_configRoot, "team-mappings"));

        var configStore = new JsonConfigStore(_configRoot);
        configStore.SaveSeasons(new List<Season>
        {
            new("season-1", "2026", "dev-sheet-2026", "active", DateTimeOffset.UtcNow)
        });
        File.WriteAllText(Path.Combine(_configRoot, "teams.json"),
            """[{"teamId":"b-squared","name":"B Squared","pin":"1111"}]""");
        configStore.SaveTeamMappings("season-1", new Dictionary<string, TeamMapping>
        {
            ["b-squared"] = new TeamMapping("2026 Keepers", "H8:N13", "C8:F13")
        });

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConfigRoot"] = _configRoot,
                    ["Google:UseDevClients"] = "true",
                    ["AdminPin"] = "9999"
                });
            });
        });
    }

    public void Dispose() => Directory.Delete(_configRoot, recursive: true);

    [Fact]
    public async Task GetKeepers_WithValidTeamPin_ReturnsTeamData()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/keepers?pin=1111");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<KeeperTeamData>(ResponseJsonOptions);
        Assert.Equal("B Squared", data!.TeamName);
    }

    [Fact]
    public async Task GetKeepers_WithInvalidPin_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/keepers?pin=0000");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutKeepers_WithInvalidContractType_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var payload = new KeeperSubmission(Enumerable.Range(0, 6)
            .Select(i => i == 0 ? new KeeperRow("New Guy", 3, 10, 2) : new KeeperRow("", null, null, null))
            .ToList());

        var response = await client.PutAsJsonAsync("/api/keepers?pin=1111&seasonId=season-1", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostAdminSeasons_WithOwnerPin_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/admin/seasons?pin=1111", new { label = "2027" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostAdminSeasons_WithAdminPin_CreatesSeason()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/admin/seasons?pin=9999", new { label = "2027" });
        response.EnsureSuccessStatusCode();
        var season = await response.Content.ReadFromJsonAsync<Season>(ResponseJsonOptions);
        Assert.Equal("2027", season!.Label);
        Assert.True(season.IsActive);
    }
}
