namespace FantasyKeeper.Api.Models;

public record ParsedWorkbook(string SheetName, IReadOnlyList<StoredTeamKeepers> Teams);
