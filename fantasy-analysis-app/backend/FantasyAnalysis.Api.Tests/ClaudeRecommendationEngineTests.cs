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

    private static readonly ScoringSettings Settings = new(
        new List<ScoringCategory> { new("homeRuns", 4m) },
        new List<ScoringCategory>(),
        new Dictionary<string, int>());

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
        var engine = new ClaudeRecommendationEngine(fakeClient, new FantasyValueRanker());

        var result = await engine.GenerateRecommendationsAsync(
            League,
            "Rhino Wranglers",
            Settings,
            new Dictionary<string, IReadOnlyList<StatLine>>(),
            new Dictionary<string, IReadOnlyList<MlbPlayer>>());

        var suggestion = Assert.Single(result.WaiverSuggestions);
        Assert.Equal("Pick up X", suggestion.Summary);
        Assert.Equal(RecommendationType.Waiver, suggestion.Type);
        Assert.Equal(1, suggestion.Rank);
        Assert.Empty(result.TradeSuggestions);
    }

    [Fact]
    public async Task GenerateRecommendationsAsync_PromptMentionsYourTeamAndOtherTeams()
    {
        var json = """{ "waiverSuggestions": [], "tradeSuggestions": [] }""";
        var fakeClient = new FakeRecommendationClient(json);
        var engine = new ClaudeRecommendationEngine(fakeClient, new FantasyValueRanker());

        await engine.GenerateRecommendationsAsync(
            League,
            "Rhino Wranglers",
            Settings,
            new Dictionary<string, IReadOnlyList<StatLine>>(),
            new Dictionary<string, IReadOnlyList<MlbPlayer>>());

        Assert.Contains("Rhino Wranglers", fakeClient.LastUserPrompt);
        Assert.Contains("Sea Dogs", fakeClient.LastUserPrompt);
        Assert.Contains("Shohei Ohtani", fakeClient.LastUserPrompt);
    }
}
