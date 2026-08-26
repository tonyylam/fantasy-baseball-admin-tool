using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

/// <summary>
/// Every category key, direction, and rate-stat component set here was verified against
/// live statsapi.mlb.com responses during design - see the design spec's category table.
/// </summary>
public static class RotoCategoryReference
{
    public static readonly IReadOnlyDictionary<string, RotoCategoryDefinition> Categories = new Dictionary<string, RotoCategoryDefinition>
    {
        ["runs"] = Counting("runs", "Runs", "hitting"),
        ["homeRuns"] = Counting("homeRuns", "Home Runs", "hitting"),
        ["rbi"] = Counting("rbi", "RBI", "hitting"),
        ["stolenBases"] = Counting("stolenBases", "Stolen Bases", "hitting"),
        ["obp"] = new("obp", "On-Base %", "hitting", StatDirection.HigherIsBetter, true,
            new[] { "hits", "baseOnBalls", "hitByPitch" }, 1m,
            new[] { "atBats", "baseOnBalls", "hitByPitch", "sacFlies" }),
        ["slg"] = new("slg", "Slugging %", "hitting", StatDirection.HigherIsBetter, true,
            new[] { "totalBases" }, 1m, new[] { "atBats" }),
        ["wins"] = Counting("wins", "Wins", "pitching"),
        ["saves"] = Counting("saves", "Saves", "pitching"),
        ["era"] = new("era", "ERA", "pitching", StatDirection.LowerIsBetter, true,
            new[] { "earnedRuns" }, 9m, new[] { "inningsPitched" }),
        ["whip"] = new("whip", "WHIP", "pitching", StatDirection.LowerIsBetter, true,
            new[] { "baseOnBalls", "hits" }, 1m, new[] { "inningsPitched" }),
        ["strikeoutsPer9Inn"] = new("strikeoutsPer9Inn", "K/9", "pitching", StatDirection.HigherIsBetter, true,
            new[] { "strikeOuts" }, 9m, new[] { "inningsPitched" }),
    };

    private static RotoCategoryDefinition Counting(string statKey, string displayName, string group) =>
        new(statKey, displayName, group, StatDirection.HigherIsBetter, false, new[] { statKey }, 1m, Array.Empty<string>());
}
