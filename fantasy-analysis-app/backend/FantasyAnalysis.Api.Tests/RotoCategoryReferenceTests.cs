using System.Linq;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class RotoCategoryReferenceTests
{
    [Theory]
    [InlineData("runs", "hitting", StatDirection.HigherIsBetter, false)]
    [InlineData("homeRuns", "hitting", StatDirection.HigherIsBetter, false)]
    [InlineData("rbi", "hitting", StatDirection.HigherIsBetter, false)]
    [InlineData("stolenBases", "hitting", StatDirection.HigherIsBetter, false)]
    [InlineData("obp", "hitting", StatDirection.HigherIsBetter, true)]
    [InlineData("slg", "hitting", StatDirection.HigherIsBetter, true)]
    [InlineData("wins", "pitching", StatDirection.HigherIsBetter, false)]
    [InlineData("saves", "pitching", StatDirection.HigherIsBetter, false)]
    [InlineData("era", "pitching", StatDirection.LowerIsBetter, true)]
    [InlineData("whip", "pitching", StatDirection.LowerIsBetter, true)]
    [InlineData("strikeoutsPer9Inn", "pitching", StatDirection.HigherIsBetter, true)]
    public void Categories_ContainsExpectedMetadataForEveryKnownCategory(
        string statKey, string group, StatDirection direction, bool isRateStat)
    {
        var definition = RotoCategoryReference.Categories[statKey];

        Assert.Equal(group, definition.Group);
        Assert.Equal(direction, definition.Direction);
        Assert.Equal(isRateStat, definition.IsRateStat);
        Assert.Equal(statKey, definition.StatKey);
    }

    [Fact]
    public void Categories_ContainsExactlyElevenSupportedCategories()
    {
        Assert.Equal(11, RotoCategoryReference.Categories.Count);
    }

    [Fact]
    public void EraWhipAndK9_UseInningsPitchedAsDenominatorWithCorrectMultiplier()
    {
        var era = RotoCategoryReference.Categories["era"];
        Assert.Equal(new[] { "earnedRuns" }, era.NumeratorStatKeys);
        Assert.Equal(9m, era.NumeratorMultiplier);
        Assert.Equal(new[] { "inningsPitched" }, era.DenominatorStatKeys);

        var whip = RotoCategoryReference.Categories["whip"];
        Assert.Equal(new[] { "baseOnBalls", "hits" }, whip.NumeratorStatKeys);
        Assert.Equal(1m, whip.NumeratorMultiplier);
        Assert.Equal(new[] { "inningsPitched" }, whip.DenominatorStatKeys);

        var k9 = RotoCategoryReference.Categories["strikeoutsPer9Inn"];
        Assert.Equal(new[] { "strikeOuts" }, k9.NumeratorStatKeys);
        Assert.Equal(9m, k9.NumeratorMultiplier);
        Assert.Equal(new[] { "inningsPitched" }, k9.DenominatorStatKeys);
    }

    [Fact]
    public void ObpAndSlg_UseCorrectUnderlyingComponents()
    {
        var obp = RotoCategoryReference.Categories["obp"];
        Assert.Equal(new[] { "hits", "baseOnBalls", "hitByPitch" }, obp.NumeratorStatKeys);
        Assert.Equal(new[] { "atBats", "baseOnBalls", "hitByPitch", "sacFlies" }, obp.DenominatorStatKeys);

        var slg = RotoCategoryReference.Categories["slg"];
        Assert.Equal(new[] { "totalBases" }, slg.NumeratorStatKeys);
        Assert.Equal(new[] { "atBats" }, slg.DenominatorStatKeys);
    }

    [Fact]
    public void CountingStats_HaveEmptyDenominatorAndNumeratorEqualToOwnStatKey()
    {
        foreach (var key in new[] { "runs", "homeRuns", "rbi", "stolenBases", "wins", "saves" })
        {
            var definition = RotoCategoryReference.Categories[key];
            Assert.Equal(new[] { key }, definition.NumeratorStatKeys);
            Assert.Empty(definition.DenominatorStatKeys);
            Assert.Equal(1m, definition.NumeratorMultiplier);
        }
    }
}
