using System;
using System.Collections.Generic;
using System.Linq;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class RotoStandingsCalculatorTests
{
    [Fact]
    public void ComputeStandings_CountingStat_SumsAcrossRosterAndRanksDescending()
    {
        var league = new League(DateTimeOffset.UtcNow, new List<TeamRoster>
        {
            new("Team A", new List<RosteredPlayer>
            {
                new("P1", "1", "P1", "OF", false),
                new("P2", "2", "P2", "OF", false)
            }),
            new("Team B", new List<RosteredPlayer> { new("P3", "3", "P3", "OF", false) })
        });
        var statsByPlayerId = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            ["1"] = new List<StatLine> { new("1", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 20m }) },
            ["2"] = new List<StatLine> { new("2", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 15m }) },
            ["3"] = new List<StatLine> { new("3", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 10m }) }
        };

        var standings = new RotoStandingsCalculator().ComputeStandings(league, new List<string> { "homeRuns" }, new List<string>(), statsByPlayerId);

        var teamA = standings.Standings.Single(s => s.TeamName == "Team A" && s.CategoryKey == "homeRuns");
        var teamB = standings.Standings.Single(s => s.TeamName == "Team B" && s.CategoryKey == "homeRuns");
        Assert.Equal(35m, teamA.Value);
        Assert.Equal(10m, teamB.Value);
        Assert.Equal(1m, teamA.Rank);
        Assert.Equal(2m, teamB.Rank);
        Assert.Equal(2m, teamA.RotoPoints);
        Assert.Equal(1m, teamB.RotoPoints);
    }

    [Fact]
    public void ComputeStandings_RateStat_RecombinesUnderlyingComponentsInsteadOfAveragingPlayerRates()
    {
        // A naive "average the players' own OBP" would be dragged way up by the tiny sample.
        // The correct team OBP recombines raw H/BB/HBP/AB/SF totals across the whole roster.
        var league = new League(DateTimeOffset.UtcNow, new List<TeamRoster>
        {
            new("Team A", new List<RosteredPlayer>
            {
                new("SmallSample", "1", "SmallSample", "OF", false),
                new("EverydayPlayer", "2", "EverydayPlayer", "OF", false)
            })
        });
        var statsByPlayerId = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            // 1-for-1 with a walk: individually a 1.000 OBP
            ["1"] = new List<StatLine> { new("1", 2026, "hitting", new Dictionary<string, decimal> { ["hits"] = 1m, ["baseOnBalls"] = 1m, ["hitByPitch"] = 0m, ["atBats"] = 1m, ["sacFlies"] = 0m }) },
            // 100-for-400 with 40 walks: individually a .318 OBP
            ["2"] = new List<StatLine> { new("2", 2026, "hitting", new Dictionary<string, decimal> { ["hits"] = 100m, ["baseOnBalls"] = 40m, ["hitByPitch"] = 0m, ["atBats"] = 400m, ["sacFlies"] = 0m }) }
        };

        var standings = new RotoStandingsCalculator().ComputeStandings(league, new List<string> { "obp" }, new List<string>(), statsByPlayerId);

        var teamObp = Assert.Single(standings.Standings).Value;
        // Correct recombination: (1+100 + 1+40) / (1+400 + 1+40) = 142/442
        var expected = 142m / 442m;
        Assert.Equal(Math.Round(expected, 4), Math.Round(teamObp, 4));
        // A naive average of the two players' own OBPs, (1.000 + 0.318) / 2 ~= 0.659, would be very different.
        Assert.NotEqual(Math.Round(0.659m, 2), Math.Round(teamObp, 2));
    }

    [Fact]
    public void ComputeStandings_LowerIsBetterCategory_RanksSmallestValueFirst()
    {
        var league = new League(DateTimeOffset.UtcNow, new List<TeamRoster>
        {
            new("Low ERA Team", new List<RosteredPlayer> { new("Ace", "1", "Ace", "SP", true) }),
            new("High ERA Team", new List<RosteredPlayer> { new("Scherzer", "2", "Scherzer", "SP", true) })
        });
        var statsByPlayerId = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            ["1"] = new List<StatLine> { new("1", 2026, "pitching", new Dictionary<string, decimal> { ["earnedRuns"] = 20m, ["inningsPitched"] = 81.0m }) },
            ["2"] = new List<StatLine> { new("2", 2026, "pitching", new Dictionary<string, decimal> { ["earnedRuns"] = 39m, ["inningsPitched"] = 50.1m }) }
        };

        var standings = new RotoStandingsCalculator().ComputeStandings(league, new List<string>(), new List<string> { "era" }, statsByPlayerId);

        var lowEra = standings.Standings.Single(s => s.TeamName == "Low ERA Team");
        var highEra = standings.Standings.Single(s => s.TeamName == "High ERA Team");
        // Lower ERA is better -> Low ERA Team ranks 1st despite having the numerically smaller value.
        Assert.Equal(1m, lowEra.Rank);
        Assert.Equal(2m, highEra.Rank);
        Assert.Equal(2m, lowEra.RotoPoints);
        Assert.Equal(1m, highEra.RotoPoints);
    }

    [Fact]
    public void ComputeStandings_TiedTeams_SplitRankAndRotoPointsEvenly()
    {
        var league = new League(DateTimeOffset.UtcNow, new List<TeamRoster>
        {
            new("Team A", new List<RosteredPlayer> { new("P1", "1", "P1", "OF", false) }),
            new("Team B", new List<RosteredPlayer> { new("P2", "2", "P2", "OF", false) }),
            new("Team C", new List<RosteredPlayer> { new("P3", "3", "P3", "OF", false) })
        });
        var statsByPlayerId = new Dictionary<string, IReadOnlyList<StatLine>>
        {
            ["1"] = new List<StatLine> { new("1", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 20m }) },
            ["2"] = new List<StatLine> { new("2", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 10m }) },
            ["3"] = new List<StatLine> { new("3", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 10m }) }
        };

        var standings = new RotoStandingsCalculator().ComputeStandings(league, new List<string> { "homeRuns" }, new List<string>(), statsByPlayerId);

        var teamA = standings.Standings.Single(s => s.TeamName == "Team A");
        var teamB = standings.Standings.Single(s => s.TeamName == "Team B");
        var teamC = standings.Standings.Single(s => s.TeamName == "Team C");
        Assert.Equal(1m, teamA.Rank);
        Assert.Equal(3m, teamA.RotoPoints);
        Assert.Equal(2.5m, teamB.Rank);
        Assert.Equal(2.5m, teamC.Rank);
        Assert.Equal(1.5m, teamB.RotoPoints);
        Assert.Equal(1.5m, teamC.RotoPoints);
    }
}
