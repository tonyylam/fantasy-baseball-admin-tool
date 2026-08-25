namespace FantasyAnalysis.Api.Models;

public record RosteredPlayer(string CsvName, string PlayerId, string PlayerFullName, string Position, bool IsPitcher);

public record TeamRoster(string TeamName, IReadOnlyList<RosteredPlayer> Players);

public record League(DateTimeOffset ImportedAtUtc, IReadOnlyList<TeamRoster> Teams);
