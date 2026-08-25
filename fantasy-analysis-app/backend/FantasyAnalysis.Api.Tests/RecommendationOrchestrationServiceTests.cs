using System.Collections.Generic;
using System.Threading.Tasks;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using FantasyAnalysis.Api.Tests.Fakes;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class RecommendationOrchestrationServiceTests
{
    private static readonly League League = new(
        System.DateTimeOffset.UtcNow,
        new List<TeamRoster>
        {
            new("Rhino Wranglers", new List<RosteredPlayer>
            {
                new("Shohei Ohtani", "660271", "Shohei Ohtani", "DH", false)
            })
        });

    private static readonly ScoringSettings Settings = new(
        new List<ScoringCategory> { new("homeRuns", 4m) },
        new List<ScoringCategory>(),
        new Dictionary<string, int>());

    private static RecommendationOrchestrationService BuildService(
        League? league,
        ScoringSettings? settings,
        out FakeRecommendationDataStore recommendationStore)
    {
        var pool = new List<MlbPlayer> { new("665742", "Juan Soto", "OF", false, 121) };
        var statsProvider = new FakeStatsProvider(pool, new List<StatLine>
        {
            new("665742", SeasonClock.Current, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 10m })
        });
        var leagueStore = new FakeLeagueDataStore();
        if (league is not null) leagueStore.SaveLeague(league);

        var responseJson = """{ "waiverSuggestions": [], "tradeSuggestions": [] }""";
        var engine = new ClaudeRecommendationEngine(new FakeRecommendationClient(responseJson), new FantasyValueRanker());
        recommendationStore = new FakeRecommendationDataStore();

        return new RecommendationOrchestrationService(
            leagueStore,
            new FakeScoringSettingsStore(settings),
            statsProvider,
            new FakeStatsCache(),
            new WaiverPoolCalculator(),
            new FantasyValueRanker(),
            engine,
            recommendationStore);
    }

    [Fact]
    public async Task RefreshAsync_NoLeagueImported_ThrowsPrerequisiteException()
    {
        var service = BuildService(null, Settings, out _);

        await Assert.ThrowsAsync<RecommendationPrerequisiteException>(() => service.RefreshAsync("Rhino Wranglers"));
    }

    [Fact]
    public async Task RefreshAsync_NoScoringSettings_ThrowsPrerequisiteException()
    {
        var service = BuildService(League, null, out _);

        await Assert.ThrowsAsync<RecommendationPrerequisiteException>(() => service.RefreshAsync("Rhino Wranglers"));
    }

    [Fact]
    public async Task RefreshAsync_HappyPath_SavesAndReturnsRecommendations()
    {
        var service = BuildService(League, Settings, out var recommendationStore);

        var result = await service.RefreshAsync("Rhino Wranglers");

        Assert.NotNull(result);
        Assert.Same(result, recommendationStore.Saved);
        Assert.Equal(result, service.GetLast());
    }
}
