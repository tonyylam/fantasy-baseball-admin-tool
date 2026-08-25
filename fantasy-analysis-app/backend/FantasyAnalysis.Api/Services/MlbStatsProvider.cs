using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
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

    public async Task<IReadOnlyList<StatLine>> GetPlayerStatsAsync(IReadOnlyList<string> playerIds, int season)
    {
        using var throttle = new SemaphoreSlim(5);
        var tasks = playerIds.Select(async playerId =>
        {
            await throttle.WaitAsync();
            try
            {
                var lines = new List<StatLine>();
                foreach (var group in new[] { "hitting", "pitching" })
                {
                    var line = await FetchGroupStatsAsync(playerId, group, season);
                    if (line is not null) lines.Add(line);
                }
                return lines;
            }
            finally
            {
                throttle.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).ToList();
    }

    private async Task<StatLine?> FetchGroupStatsAsync(string playerId, string group, int season)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync($"api/v1/people/{playerId}/stats?stats=season&group={group}&season={season}");
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            throw new StatsProviderException($"Failed to reach the MLB Stats API for player {playerId} ({group}).", ex);
        }

        var body = await response.Content.ReadAsStringAsync();
        StatsResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<StatsResponse>(body);
        }
        catch (JsonException ex)
        {
            throw new StatsProviderException($"MLB Stats API stats response for player {playerId} was not valid JSON.", ex);
        }

        if (parsed?.Stats is null)
        {
            throw new StatsProviderException($"MLB Stats API stats response for player {playerId} ({group}) did not contain a \"stats\" array.");
        }

        var split = parsed.Stats.SelectMany(s => s.Splits ?? new List<SplitDto>()).FirstOrDefault();
        if (split?.Stat is null || split.Stat.Count == 0) return null;

        var stats = new Dictionary<string, decimal>();
        foreach (var (key, element) in split.Stat)
        {
            var value = TryConvertToDecimal(element);
            if (value is not null) stats[key] = value.Value;
        }

        return new StatLine(playerId, season, group, stats);
    }

    private static decimal? TryConvertToDecimal(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => element.GetDecimal(),
        JsonValueKind.String when decimal.TryParse(element.GetString(), out var parsed) => parsed,
        _ => null
    };

    private class StatsResponse
    {
        [JsonPropertyName("stats")]
        public List<StatGroupDto>? Stats { get; set; }
    }

    private class StatGroupDto
    {
        [JsonPropertyName("splits")]
        public List<SplitDto>? Splits { get; set; }
    }

    private class SplitDto
    {
        [JsonPropertyName("stat")]
        public Dictionary<string, JsonElement>? Stat { get; set; }
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
