using System.Collections.Generic;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class RotoStatMathTests
{
    [Theory]
    [InlineData(50.0, 50.0)]      // whole innings, no conversion needed
    [InlineData(50.1, 50.333333)] // ".1" means 1/3 of an inning, not 0.1
    [InlineData(50.2, 50.666667)] // ".2" means 2/3 of an inning, not 0.2
    public void ConvertToTrueInnings_HandlesMlbThirdsNotation(double raw, double expected)
    {
        var result = RotoStatMath.ConvertToTrueInnings((decimal)raw);

        Assert.Equal((decimal)expected, System.Math.Round(result, 6));
    }

    [Fact]
    public void ComputeCategoryValue_CountingStat_SumsAcrossLinesFromMatchingGroupOnly()
    {
        var definition = RotoCategoryReference.Categories["homeRuns"];
        var lines = new List<StatLine>
        {
            new("1", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 20m }),
            new("1", 2026, "hitting", new Dictionary<string, decimal> { ["homeRuns"] = 15m }),
            new("1", 2026, "pitching", new Dictionary<string, decimal> { ["homeRuns"] = 999m }) // wrong group, must be ignored
        };

        var value = RotoStatMath.ComputeCategoryValue(lines, definition);

        Assert.Equal(35m, value);
    }

    [Fact]
    public void ComputeCategoryValue_RateStatWithMultiplier_RecombinesUnderlyingComponentsAndConvertsInnings()
    {
        var definition = RotoCategoryReference.Categories["era"];
        var lines = new List<StatLine>
        {
            // 20 ER over 81.0 IP
            new("1", 2026, "pitching", new Dictionary<string, decimal> { ["earnedRuns"] = 20m, ["inningsPitched"] = 81.0m }),
            // 19 ER over "50.1" (= 50 + 1/3) IP
            new("1", 2026, "pitching", new Dictionary<string, decimal> { ["earnedRuns"] = 19m, ["inningsPitched"] = 50.1m })
        };

        var value = RotoStatMath.ComputeCategoryValue(lines, definition);

        // total ER = 39, total true IP = 81 + 50 + 1/3 = 131.333...; ERA = 39*9/131.333... = 2.6725...
        var expected = 39m * 9m / (131m + 1m / 3m);
        Assert.Equal(System.Math.Round(expected, 4), System.Math.Round(value!.Value, 4));
    }

    [Fact]
    public void ComputeCategoryValue_RateStatWithZeroDenominator_ReturnsNull()
    {
        var definition = RotoCategoryReference.Categories["obp"];
        var lines = new List<StatLine>(); // no stat lines at all -> denominator sums to zero

        var value = RotoStatMath.ComputeCategoryValue(lines, definition);

        Assert.Null(value);
    }
}
