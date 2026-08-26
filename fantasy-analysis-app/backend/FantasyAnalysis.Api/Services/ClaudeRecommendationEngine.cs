using System.Linq;
using System.Text.Json;
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class ClaudeRecommendationEngine
{
    private readonly IRecommendationClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ClaudeRecommendationEngine(IRecommendationClient client)
    {
        _client = client;
    }

    public async Task<RecommendationSet> GenerateRecommendationsAsync(
        League league,
        string yourTeamName,
        RotoStandings standings,
        IReadOnlyDictionary<string, IReadOnlyList<MlbPlayer>> weakCategoryShortlist,
        IReadOnlyDictionary<string, IReadOnlyList<StatLine>> statsByPlayerId)
    {
        var systemPrompt =
            "You are a fantasy baseball analyst for a Rotisserie-style league: every team is ranked " +
            "1st-to-last in each scoring category and awarded points by rank, summed for an overall " +
            "standing. Given the league's current category standings, one team's weakest categories " +
            "with a shortlist of available waiver-wire candidates strong in those categories, and " +
            "every team's roster, recommend waiver pickups and trades that would improve the given " +
            "team's standing. Use web search to check recent news, injuries, or performance trends " +
            "that could affect a recommendation, and cite any URLs you used. Respond only with JSON " +
            "matching the provided schema.";

        var userPrompt = BuildUserPrompt(league, yourTeamName, standings, weakCategoryShortlist, statsByPlayerId);

        var json = await _client.GetRecommendationsJsonAsync(systemPrompt, userPrompt);
        return ParseResponse(json);
    }

    private static string BuildUserPrompt(
        League league,
        string yourTeamName,
        RotoStandings standings,
        IReadOnlyDictionary<string, IReadOnlyList<MlbPlayer>> weakCategoryShortlist,
        IReadOnlyDictionary<string, IReadOnlyList<StatLine>> statsByPlayerId)
    {
        object RosterPayload(RosteredPlayer p) => new { playerId = p.PlayerId, fullName = p.PlayerFullName, position = p.Position };

        var yourTeam = league.Teams.First(t => t.TeamName == yourTeamName);
        var otherTeams = league.Teams.Where(t => t.TeamName != yourTeamName);

        var payload = new
        {
            yourTeam = new { teamName = yourTeam.TeamName, players = yourTeam.Players.Select(RosterPayload) },
            otherTeams = otherTeams.Select(t => new { teamName = t.TeamName, players = t.Players.Select(RosterPayload) }),
            standings = standings.Standings.Select(s => new
            {
                teamName = s.TeamName,
                category = s.CategoryKey,
                value = s.Value,
                rank = s.Rank,
                rotoPoints = s.RotoPoints
            }),
            weakCategoryShortlist = weakCategoryShortlist.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Select(p => new
                {
                    playerId = p.Id,
                    fullName = p.FullName,
                    position = p.Position,
                    categoryValue = RotoStatMath.ComputeCategoryValue(
                        statsByPlayerId.TryGetValue(p.Id, out var lines) ? lines : Array.Empty<StatLine>(),
                        RotoCategoryReference.Categories[kv.Key])
                }))
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
