using System.Collections.Generic;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class FantasyValueRankerTests
{
    private static readonly ScoringSettings Settings = new(
        new List<ScoringCategory> { new("homeRuns", 4m), new("stolenBases", 2m) },
        new List<ScoringCategory> { new("strikeOuts", 1m) },
        new Dictionary<string, int>());

    [Fact]
    public void ComputePlayerValue_SumsAcrossMatchingCategories()
    {
        var ranker = new FantasyValueRanker();
        var lines = new List<StatLine>
        {
            new("1", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 10m, ["stolenBases"] = 5m, ["hits"] = 100m })
        };

        var value = ranker.ComputePlayerValue(lines, Settings);

        Assert.Equal(50m, value); // 10*4 + 5*2; "hits" isn't a scored category, ignored
    }

    [Fact]
    public void TopCandidatesByPosition_GroupsAndRanksWithinEachPosition()
    {
        var ranker = new FantasyValueRanker();
        var candidates = new List<MlbPlayer>
        {
            new("1", "Low OF", "OF", false, 100),
            new("2", "High OF", "OF", false, 100),
            new("3", "Only SS", "SS", false, 100)
        };
        var stats = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            ["1"] = new List<StatLine> { new("1", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 1m }) },
            ["2"] = new List<StatLine> { new("2", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 10m }) },
            ["3"] = new List<StatLine> { new("3", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 3m }) }
        };

        var result = ranker.TopCandidatesByPosition(candidates, stats, Settings, topNPerPosition: 1);

        Assert.Equal("High OF", result["OF"][0].FullName);
        Assert.Equal("Only SS", result["SS"][0].FullName);
    }
}
