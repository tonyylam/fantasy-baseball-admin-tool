namespace FantasyAnalysis.Api.Models;

public record StatsCacheEntry(DateTimeOffset FetchedAtUtc, IReadOnlyList<StatLine> StatLines);
