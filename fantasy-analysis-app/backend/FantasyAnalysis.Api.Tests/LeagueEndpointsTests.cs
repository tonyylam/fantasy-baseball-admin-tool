using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using FantasyAnalysis.Api.Tests.Fakes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class LeagueEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LeagueEndpointsTests(WebApplicationFactory<Program> factory)
    {
        var pool = new List<MlbPlayer> { new("660271", "Shohei Ohtani", "DH", false, 119) };
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStatsProvider>(new FakeStatsProvider(pool));
            services.AddSingleton<ILeagueDataStore>(new FakeLeagueDataStore());
        }));
    }

    [Fact]
    public async Task GetLeague_WhenNoneImported_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/league");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ImportThenConfirm_PersistsLeagueRetrievableViaGet()
    {
        var client = _factory.CreateClient();
        using var form = new MultipartFormDataContent();
        var csvContent = new StringContent("Team,Player\nRhino Wranglers,Shohei Ohtani\n");
        form.Add(csvContent, "file", "roster.csv");

        var importResponse = await client.PostAsync("/api/league/import", form);
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var preview = await importResponse.Content.ReadFromJsonAsync<ImportPreview>();

        var bestGuess = preview!.Teams[0].Players[0].BestGuess!;
        var confirmRequest = new ConfirmImportRequest(new List<ConfirmedTeam>
        {
            new("Rhino Wranglers", new List<ConfirmedPlayer>
            {
                new("Shohei Ohtani", bestGuess.PlayerId, bestGuess.FullName, bestGuess.Position, bestGuess.IsPitcher)
            })
        });

        var confirmResponse = await client.PostAsJsonAsync("/api/league/import/confirm", confirmRequest);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

        var getResponse = await client.GetAsync("/api/league");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var league = await getResponse.Content.ReadFromJsonAsync<League>();
        Assert.Single(league!.Teams);
    }
}
