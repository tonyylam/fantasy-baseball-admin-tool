using System.Linq;
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public static class RotoStatMath
{
    // MLB reports innings pitched as e.g. "50.1", where the digit after the decimal is
    // THIRDS of an inning (.1 = 1/3, .2 = 2/3) - not a decimal fraction. Getting this wrong
    // would silently skew every ERA/WHIP/K9 computation that sums innings across players.
    public static decimal ConvertToTrueInnings(decimal rawValue)
    {
        var wholeInnings = decimal.Truncate(rawValue);
        var fractionalDigit = rawValue - wholeInnings;
        if (fractionalDigit == 0.1m) return wholeInnings + 1m / 3m;
        if (fractionalDigit == 0.2m) return wholeInnings + 2m / 3m;
        return wholeInnings;
    }

    // Computes a category's value (counting or rate) from any set of stat lines - a single
    // candidate's own line(s), or every rostered player's lines concatenated together for a
    // team total. Summation is associative, so one formula serves both callers.
    public static decimal? ComputeCategoryValue(IEnumerable<StatLine> lines, RotoCategoryDefinition definition)
    {
        var relevantLines = lines.Where(l => l.Group == definition.Group).ToList();

        var numerator = definition.NumeratorStatKeys.Sum(key => SumStatKey(relevantLines, key)) * definition.NumeratorMultiplier;

        if (definition.DenominatorStatKeys.Count == 0)
        {
            return numerator;
        }

        var denominator = definition.DenominatorStatKeys.Sum(key => SumStatKey(relevantLines, key));
        return denominator == 0 ? null : numerator / denominator;
    }

    private static decimal SumStatKey(IReadOnlyList<StatLine> lines, string statKey)
    {
        decimal total = 0;
        foreach (var line in lines)
        {
            if (!line.Stats.TryGetValue(statKey, out var rawValue)) continue;
            total += statKey == "inningsPitched" ? ConvertToTrueInnings(rawValue) : rawValue;
        }
        return total;
    }
}
