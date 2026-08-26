namespace FantasyAnalysis.Api.Models;

public record ScoringSettings(
    IReadOnlyList<string> HittingCategoryKeys,
    IReadOnlyList<string> PitchingCategoryKeys,
    IReadOnlyDictionary<string, int> RosterSlots);
