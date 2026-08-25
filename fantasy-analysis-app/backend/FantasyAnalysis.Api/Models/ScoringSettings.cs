namespace FantasyAnalysis.Api.Models;

public record ScoringCategory(string StatKey, decimal PointsPerUnit);

public record ScoringSettings(
    IReadOnlyList<ScoringCategory> HittingCategories,
    IReadOnlyList<ScoringCategory> PitchingCategories,
    IReadOnlyDictionary<string, int> RosterSlots);
