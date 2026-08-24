using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class KeeperEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly JsonSerializerOptions ResponseJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _configRoot;
    private readonly string _dataRoot;

    public KeeperEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _configRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _dataRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_configRoot);
        Directory.CreateDirectory(_dataRoot);

        File.WriteAllText(Path.Combine(_configRoot, "teams.json"),
            """[{"teamId":"b-squared","name":"B Squared","pin":"1111"},{"teamId":"ba-bombers","name":"BA Bombers","pin":"2222"}]""");

        var dataStore = new FileKeepersDataStore(_dataRoot);
        dataStore.SaveData(new KeepersData(
            "test.xlsx",
            "2026 Keepers",
            DateTimeOffset.UtcNow,
            new Dictionary<string, StoredTeamKeepers>
            {
                ["b-squared"] = new StoredTeamKeepers(
                    "B Squared",
                    7,
                    new List<int> { 8, 9, 10, 11, 12, 13 },
                    new List<KeeperRow>
                    {
                        new("T. Story", 1, 14, 2),
                        new("", null, null, null),
                        new("", null, null, null),
                        new("", null, null, null),
                        new("", null, null, null),
                        new("", null, null, null)
                    },
                    new List<int> { 20 },
                    new List<ExistingContractRow>
                    {
                        new("Jasson Dominguez", "#1 - 2/3", 3, 1.34m, 1.34m)
                    }),
                ["ba-bombers"] = new StoredTeamKeepers(
                    "BA Bombers",
                    30,
                    new List<int> { 31 },
                    new List<KeeperRow> { new("", null, null, null) },
                    new List<int>(),
                    new List<ExistingContractRow>())
            }));

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConfigRoot"] = _configRoot,
                    ["DataRoot"] = _dataRoot,
                    ["AdminPin"] = "9999"
                });
            });
        });
    }

    public void Dispose()
    {
        Directory.Delete(_configRoot, recursive: true);
        Directory.Delete(_dataRoot, recursive: true);
    }

    [Fact]
    public async Task GetKeepers_OwnTeam_ReturnsCanEditTrue()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/keepers?pin=1111&teamId=b-squared");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<KeeperTeamData>(ResponseJsonOptions);
        Assert.Equal("B Squared", data!.TeamName);
        Assert.True(data.CanEdit);
    }

    [Fact]
    public async Task GetKeepers_OtherTeam_ReturnsCanEditFalse()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/keepers?pin=1111&teamId=ba-bombers");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<KeeperTeamData>(ResponseJsonOptions);
        Assert.Equal("BA Bombers", data!.TeamName);
        Assert.False(data.CanEdit);
    }

    [Fact]
    public async Task GetKeepers_AdminAnyTeam_ReturnsCanEditTrue()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/keepers?pin=9999&teamId=ba-bombers");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<KeeperTeamData>(ResponseJsonOptions);
        Assert.True(data!.CanEdit);
    }

    [Fact]
    public async Task GetKeepers_WithInvalidPin_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/keepers?pin=0000&teamId=b-squared");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetKeepers_MissingTeamId_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/keepers?pin=1111");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutKeepers_WithInvalidContractType_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var payload = new KeeperSubmission(
            Enumerable.Range(0, 6)
                .Select(i => i == 0 ? new KeeperRow("New Guy", 3, 10, 2) : new KeeperRow("", null, null, null))
                .ToList(),
            new List<int>());

        var response = await client.PutAsJsonAsync("/api/keepers?pin=1111&teamId=b-squared", payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutKeepers_ValidSubmission_PersistsAndReturnsUpdatedData()
    {
        var client = _factory.CreateClient();
        var payload = new KeeperSubmission(
            Enumerable.Range(0, 6)
                .Select(i => i == 0 ? new KeeperRow("New Guy", 1, 10, 2) : new KeeperRow("", null, null, null))
                .ToList(),
            new List<int>());

        var response = await client.PutAsJsonAsync("/api/keepers?pin=1111&teamId=b-squared", payload);
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<KeeperTeamData>(ResponseJsonOptions);
        Assert.Equal("New Guy", data!.NewContracts[0].Player);
    }

    [Fact]
    public async Task PutKeepers_OtherTeam_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        var payload = new KeeperSubmission(new List<KeeperRow> { new("", null, null, null) }, new List<int>());

        var response = await client.PutAsJsonAsync("/api/keepers?pin=1111&teamId=ba-bombers", payload);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PutKeepers_AdminOtherTeam_Succeeds()
    {
        var client = _factory.CreateClient();
        var payload = new KeeperSubmission(new List<KeeperRow> { new("Admin Pick", 1, 5, 1) }, new List<int>());

        var response = await client.PutAsJsonAsync("/api/keepers?pin=9999&teamId=ba-bombers", payload);
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<KeeperTeamData>(ResponseJsonOptions);
        Assert.Equal("Admin Pick", data!.NewContracts[0].Player);
    }

    [Fact]
    public async Task GetTeams_WithOwnerPin_ReturnsTeams()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/teams?pin=1111");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, json.GetArrayLength());
    }

    [Fact]
    public async Task GetTeams_WithAdminPin_ReturnsTeams()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/teams?pin=9999");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, json.GetArrayLength());
    }
}
