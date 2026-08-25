namespace FantasyAnalysis.Api.Models;

public record PlayerMatchCandidate(string PlayerId, string FullName, string Position, bool IsPitcher, double Score);

public record PlayerMatch(string CsvName, PlayerMatchCandidate? BestGuess, IReadOnlyList<PlayerMatchCandidate> Candidates);
