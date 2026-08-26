namespace FantasyAnalysis.Api.Models;

public record TeamCategoryStanding(string TeamName, string CategoryKey, decimal Value, decimal Rank, decimal RotoPoints);

public record RotoStandings(IReadOnlyList<TeamCategoryStanding> Standings);
