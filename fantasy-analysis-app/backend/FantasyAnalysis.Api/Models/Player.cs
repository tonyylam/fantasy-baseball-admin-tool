namespace FantasyAnalysis.Api.Models;

public record ParsedLeague(IReadOnlyList<ParsedTeamRoster> Teams);

public record ParsedTeamRoster(string TeamName, IReadOnlyList<ParsedPlayer> Players);

public record ParsedPlayer(string PlayerName, string? Position, string? ProTeamAbbreviation);
