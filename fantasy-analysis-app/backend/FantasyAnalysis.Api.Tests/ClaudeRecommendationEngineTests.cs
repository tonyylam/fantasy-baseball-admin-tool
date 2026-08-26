using System.Collections.Generic;
using System.Threading.Tasks;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using FantasyAnalysis.Api.Tests.Fakes;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class ClaudeRecommendationEngineTests
{
    private static readonly League League = new(
        System.DateTimeOffset.UtcNow,
        new List<TeamRoster>
        {
            new("Rhino Wranglers", new List<RosteredPlayer>
            {
                new("Shohei Ohtani", "660271", "Shohei Ohtani", "DH", false)
            }),
            new("Sea Dogs", new List<RosteredPlayer>
            {
                new("Juan Soto", "665742", "Juan Soto", "OF", false)
            })
        });

    private static readonly RotoStandings Standings = new(new List<TeamCategoryStanding>
    {
        new("Rhino Wranglers", "homeRuns", 10m, 2m, 1m),
        new("Sea Dogs", "homeRuns", 20m, 1m, 2m)
    });

    [Fact]
    public async Task GenerateRecommendationsAsync_ParsesClientJsonIntoRecommendationSet()
    {
        var json = """
        {
          "waiverSuggestions": [
            { "summary": "Pick up X", "reasoning": "Hot streak", "involvedPlayerIds": ["123"], "citations": ["https://example.com"] }
          ],
          "tradeSuggestions": []
        }
        """;
        var fakeClient = new FakeRecommendationClient(json);
        var engine = new ClaudeRecommendationEngine(fakeClient);

        var result = await engine.GenerateRecommendationsAsync(
            League,
            "Rhino Wranglers",
            Standings,
            new Dictionary<string, IReadOnlyList<MlbPlayer>>(),
            new Dictionary<string, IReadOnlyList<StatLine>>());

        var suggestion = Assert.Single(result.WaiverSuggestions);
        Assert.Equal("Pick up X", suggestion.Summary);
        Assert.Equal(RecommendationType.Waiver, suggestion.Type);
        Assert.Equal(1, suggestion.Rank);
        Assert.Empty(result.TradeSuggestions);
    }

    [Fact]
    public async Task GenerateRecommendationsAsync_PromptMentionsTeamsAndWeakCategoryStandings()
    {
        var json = """{ "waiverSuggestions": [], "tradeSuggestions": [] }""";
        var fakeClient = new FakeRecommendationClient(json);
        var engine = new ClaudeRecommendationEngine(fakeClient);
        var shortlist = new Dictionary<string, IReadOnlyList<MlbPlayer>>
        {
            ["homeRuns"] = new List<MlbPlayer> { new("999", "Waiver Guy", "OF", false, 108) }
        };
        var statsByPlayerId = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            ["999"] = new List<StatLine> { new("999", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 30m }) }
        };

        await engine.GenerateRecommendationsAsync(League, "Rhino Wranglers", Standings, shortlist, statsByPlayerId);

        Assert.Contains("Rhino Wranglers", fakeClient.LastUserPrompt);
        Assert.Contains("Sea Dogs", fakeClient.LastUserPrompt);
        Assert.Contains("Waiver Guy", fakeClient.LastUserPrompt);
        Assert.Contains("homeRuns", fakeClient.LastUserPrompt);
    }
}
