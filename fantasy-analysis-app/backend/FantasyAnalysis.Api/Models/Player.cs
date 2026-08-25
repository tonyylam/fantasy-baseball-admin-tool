namespace FantasyAnalysis.Api.Models;

public record ParsedLeague(IReadOnlyList<ParsedTeamRoster> Teams);

public record ParsedTeamRoster(string TeamName, IReadOnlyList<string> PlayerNames);
