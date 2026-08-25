namespace FantasyAnalysis.Api.Models;

public enum RecommendationType { Waiver, Trade }

public record Recommendation(
    RecommendationType Type,
    string Summary,
    string Reasoning,
    IReadOnlyList<string> InvolvedPlayerIds,
    IReadOnlyList<string> Citations,
    int Rank);

public record RecommendationSet(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<Recommendation> WaiverSuggestions,
    IReadOnlyList<Recommendation> TradeSuggestions);
