namespace FantasyAnalysis.Api.Models;

public record TeamMatchPreview(string TeamName, IReadOnlyList<PlayerMatch> Players);

public record ImportPreview(IReadOnlyList<TeamMatchPreview> Teams);

public record ConfirmedPlayer(string CsvName, string? PlayerId, string? PlayerFullName, string? Position, bool IsPitcher);

public record ConfirmedTeam(string TeamName, IReadOnlyList<ConfirmedPlayer> Players);

public record ConfirmImportRequest(IReadOnlyList<ConfirmedTeam> Teams);
