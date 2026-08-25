using System.Text.Json;
using System.Text.Json.Serialization;
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class MlbStatsProvider : IStatsProvider
{
    private readonly HttpClient _http;

    public MlbStatsProvider(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<MlbPlayer>> GetAllActivePlayersAsync(int season)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync($"api/v1/sports/1/players?season={season}");
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            throw new StatsProviderException("Failed to reach the MLB Stats API for the player list.", ex);
        }

        var body = await response.Content.ReadAsStringAsync();
        PlayersResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<PlayersResponse>(body);
        }
        catch (JsonException ex)
        {
            throw new StatsProviderException("MLB Stats API player list response was not valid JSON.", ex);
        }

        if (parsed?.People is null)
        {
            throw new StatsProviderException("MLB Stats API player list response did not contain a \"people\" array.");
        }

        return parsed.People
            .Where(p => p.Active && p.Id is not null && p.FullName is not null)
            .Select(p => new MlbPlayer(
                p.Id!.Value.ToString(),
                p.FullName!,
                p.PrimaryPosition?.Abbreviation ?? "",
                string.Equals(p.PrimaryPosition?.Type, "Pitcher", StringComparison.OrdinalIgnoreCase),
                p.CurrentTeam?.Id))
            .ToList();
    }

    public Task<IReadOnlyList<StatLine>> GetPlayerStatsAsync(IReadOnlyList<string> playerIds, int season)
    {
        throw new NotImplementedException("Implemented in Task 5.");
    }

    private class PlayersResponse
    {
        [JsonPropertyName("people")]
        public List<PersonDto>? People { get; set; }
    }

    private class PersonDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("fullName")]
        public string? FullName { get; set; }

        [JsonPropertyName("active")]
        public bool Active { get; set; }

        [JsonPropertyName("primaryPosition")]
        public PositionDto? PrimaryPosition { get; set; }

        [JsonPropertyName("currentTeam")]
        public TeamDto? CurrentTeam { get; set; }
    }

    private class PositionDto
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("abbreviation")]
        public string? Abbreviation { get; set; }
    }

    private class TeamDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }
    }
}
