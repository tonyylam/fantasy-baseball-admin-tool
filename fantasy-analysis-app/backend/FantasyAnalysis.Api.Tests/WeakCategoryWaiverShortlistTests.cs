using System.Collections.Generic;
using System.Linq;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class WeakCategoryWaiverShortlistTests
{
    private const string YourTeam = "Rhino Wranglers";

    private static RotoStandings StandingsWithWeakCategories(params string[] weakestFirstCategoryKeys)
    {
        // Build standings where YourTeam's rank gets progressively worse for each key listed,
        // so the weakest categories are exactly the ones the test names, in that order.
        var entries = weakestFirstCategoryKeys
            .Select((key, index) => new TeamCategoryStanding(YourTeam, key, 0m, Rank: 10m - index, RotoPoints: index))
            .ToList();
        return new RotoStandings(entries);
    }

    [Fact]
    public void ShortlistForWeakCategories_IdentifiesTheThreeWorstRankedCategories()
    {
        var standings = StandingsWithWeakCategories("stolenBases", "era", "whip", "homeRuns");
        var pool = new List<MlbPlayer> { new("1", "Speedy Guy", "OF", false, 108) };
        var stats = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            ["1"] = new List<StatLine> { new("1", 2026, "hitting", new Dictionary<string, decimal> { ["stolenBases"] = 40m }) }
        };

        var shortlist = new WeakCategoryWaiverShortlist().ShortlistForWeakCategories(standings, YourTeam, pool, stats);

        Assert.Equal(new[] { "stolenBases", "era", "whip" }.OrderBy(x => x), shortlist.Keys.OrderBy(x => x));
    }

    [Fact]
    public void ShortlistForWeakCategories_CountingCategory_RanksCandidatesByRawProductionDescending()
    {
        var standings = StandingsWithWeakCategories("stolenBases");
        var pool = new List<MlbPlayer>
        {
            new("1", "Slow Guy", "OF", false, 108),
            new("2", "Fast Guy", "OF", false, 108)
        };
        var stats = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            ["1"] = new List<StatLine> { new("1", 2026, "hitting", new Dictionary<string, decimal> { ["stolenBases"] = 2m }) },
            ["2"] = new List<StatLine> { new("2", 2026, "hitting", new Dictionary<string, decimal> { ["stolenBases"] = 25m }) }
        };

        var shortlist = new WeakCategoryWaiverShortlist().ShortlistForWeakCategories(standings, YourTeam, pool, stats);

        Assert.Equal("Fast Guy", shortlist["stolenBases"][0].FullName);
    }

    [Fact]
    public void ShortlistForWeakCategories_LowerIsBetterCategory_RanksCandidatesAscending()
    {
        var standings = StandingsWithWeakCategories("era");
        var pool = new List<MlbPlayer>
        {
            new("1", "Bad ERA", "SP", true, 108),
            new("2", "Good ERA", "SP", true, 108)
        };
        var stats = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            ["1"] = new List<StatLine> { new("1", 2026, "pitching", new Dictionary<string, decimal> { ["earnedRuns"] = 50m, ["inningsPitched"] = 100m }) },
            ["2"] = new List<StatLine> { new("2", 2026, "pitching", new Dictionary<string, decimal> { ["earnedRuns"] = 20m, ["inningsPitched"] = 100m }) }
        };

        var shortlist = new WeakCategoryWaiverShortlist().ShortlistForWeakCategories(standings, YourTeam, pool, stats);

        Assert.Equal("Good ERA", shortlist["era"][0].FullName);
    }

    [Fact]
    public void ShortlistForWeakCategories_RateStatBelowSampleSizeFloor_IsExcluded()
    {
        var standings = StandingsWithWeakCategories("obp");
        var pool = new List<MlbPlayer>
        {
            new("1", "Tiny Sample Hot Streak", "OF", false, 108),
            new("2", "Real Season", "OF", false, 108)
        };
        var stats = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            // 3-for-3, perfect but only 3 plate appearances - below the 50 PA floor.
            ["1"] = new List<StatLine> { new("1", 2026, "hitting", new Dictionary<string, decimal> { ["hits"] = 3m, ["baseOnBalls"] = 0m, ["hitByPitch"] = 0m, ["atBats"] = 3m, ["sacFlies"] = 0m, ["plateAppearances"] = 3m }) },
            // A real, unremarkable OBP over a full sample.
            ["2"] = new List<StatLine> { new("2", 2026, "hitting", new Dictionary<string, decimal> { ["hits"] = 90m, ["baseOnBalls"] = 30m, ["hitByPitch"] = 0m, ["atBats"] = 350m, ["sacFlies"] = 0m, ["plateAppearances"] = 380m }) }
        };

        var shortlist = new WeakCategoryWaiverShortlist().ShortlistForWeakCategories(standings, YourTeam, pool, stats);

        Assert.DoesNotContain(shortlist["obp"], p => p.FullName == "Tiny Sample Hot Streak");
        Assert.Contains(shortlist["obp"], p => p.FullName == "Real Season");
    }

    [Fact]
    public void ShortlistForWeakCategories_FewerCandidatesThanTheCap_ReturnsAllAvailableWithoutError()
    {
        var standings = StandingsWithWeakCategories("homeRuns");
        var pool = new List<MlbPlayer> { new("1", "Only Candidate", "OF", false, 108) };
        var stats = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            ["1"] = new List<StatLine> { new("1", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 5m }) }
        };

        var shortlist = new WeakCategoryWaiverShortlist().ShortlistForWeakCategories(standings, YourTeam, pool, stats);

        Assert.Single(shortlist["homeRuns"]);
    }
}
