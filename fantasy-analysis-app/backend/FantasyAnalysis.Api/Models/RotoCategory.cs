namespace FantasyAnalysis.Api.Models;

public enum StatDirection { HigherIsBetter, LowerIsBetter }

public record RotoCategoryDefinition(
    string StatKey,
    string DisplayName,
    string Group,
    StatDirection Direction,
    bool IsRateStat,
    IReadOnlyList<string> NumeratorStatKeys,
    decimal NumeratorMultiplier,
    IReadOnlyList<string> DenominatorStatKeys);
