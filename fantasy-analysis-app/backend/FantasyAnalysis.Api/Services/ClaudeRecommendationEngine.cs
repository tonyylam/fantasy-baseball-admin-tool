using System.Text.Json;
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class ClaudeRecommendationEngine
{
    private readonly IRecommendationClient _client;
    private readonly FantasyValueRanker _ranker;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ClaudeRecommendationEngine(IRecommendationClient client, FantasyValueRanker ranker)
    {
        _client = client;
        _ranker = ranker;
    }

    public async Task<RecommendationSet> GenerateRecommendationsAsync(
        League league,
        string yourTeamName,
        ScoringSettings settings,
        IReadOnlyDictionary<string, IReadOnlyList<StatLine>> statsByPlayerId,
        IReadOnlyDictionary<string, IReadOnlyList<MlbPlayer>> waiverShortlistByPosition)
    {
        var systemPrompt =
            "You are a fantasy baseball analyst. Given one team's roster, every other team's roster, " +
            "a shortlist of available waiver-wire candidates, and the league's scoring settings, " +
            "recommend waiver pickups and trades that would improve the given team. Use web search " +
            "to check recent news, injuries, or performance trends that could affect a recommendation, " +
            "and cite any URLs you used. Respond only with JSON matching the provided schema.";

        var userPrompt = BuildUserPrompt(league, yourTeamName, settings, statsByPlayerId, waiverShortlistByPosition);

        var json = await _client.GetRecommendationsJsonAsync(systemPrompt, userPrompt);
        return ParseResponse(json);
    }

    private string BuildUserPrompt(
        League league,
        string yourTeamName,
        ScoringSettings settings,
        IReadOnlyDictionary<string, IReadOnlyList<StatLine>> statsByPlayerId,
        IReadOnlyDictionary<string, IReadOnlyList<MlbPlayer>> waiverShortlistByPosition)
    {
        object PlayerPayload(RosteredPlayer p) => new
        {
            playerId = p.PlayerId,
            fullName = p.PlayerFullName,
            position = p.Position,
            fantasyValue = _ranker.ComputePlayerValue(
                statsByPlayerId.TryGetValue(p.PlayerId, out var lines) ? lines : Array.Empty<StatLine>(),
                settings)
        };

        var yourTeam = league.Teams.First(t => t.TeamName == yourTeamName);
        var otherTeams = league.Teams.Where(t => t.TeamName != yourTeamName);

        var payload = new
        {
            yourTeam = new { teamName = yourTeam.TeamName, players = yourTeam.Players.Select(PlayerPayload) },
            otherTeams = otherTeams.Select(t => new { teamName = t.TeamName, players = t.Players.Select(PlayerPayload) }),
            waiverShortlistByPosition = waiverShortlistByPosition.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Select(p => new
                {
                    playerId = p.Id,
                    fullName = p.FullName,
                    position = p.Position,
                    fantasyValue = _ranker.ComputePlayerValue(
                        statsByPlayerId.TryGetValue(p.Id, out var lines) ? lines : Array.Empty<StatLine>(),
                        settings)
                })),
            scoringSettings = settings
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static RecommendationSet ParseResponse(string json)
    {
        ClaudeRecommendationSetDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ClaudeRecommendationSetDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new RecommendationClientException("Claude's recommendation response was not valid JSON.", ex);
        }

        if (dto is null)
        {
            throw new RecommendationClientException("Claude's recommendation response deserialized to null.");
        }

        IReadOnlyList<Recommendation> ToRecommendations(List<ClaudeRecommendationDto>? items, RecommendationType type) =>
            (items ?? new List<ClaudeRecommendationDto>())
                .Select((item, index) => new Recommendation(
                    type,
                    item.Summary,
                    item.Reasoning,
                    item.InvolvedPlayerIds ?? new List<string>(),
                    item.Citations ?? new List<string>(),
                    index + 1))
                .ToList();

        return new RecommendationSet(
            DateTimeOffset.UtcNow,
            ToRecommendations(dto.WaiverSuggestions, RecommendationType.Waiver),
            ToRecommendations(dto.TradeSuggestions, RecommendationType.Trade));
    }

    private class ClaudeRecommendationDto
    {
        public string Summary { get; set; } = "";
        public string Reasoning { get; set; } = "";
        public List<string>? InvolvedPlayerIds { get; set; }
        public List<string>? Citations { get; set; }
    }

    private class ClaudeRecommendationSetDto
    {
        public List<ClaudeRecommendationDto>? WaiverSuggestions { get; set; }
        public List<ClaudeRecommendationDto>? TradeSuggestions { get; set; }
    }
}
