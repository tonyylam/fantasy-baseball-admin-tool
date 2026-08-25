namespace FantasyAnalysis.Api.Models;

public record StatLine(string PlayerId, int Season, string Group, IReadOnlyDictionary<string, decimal> Stats);
