namespace FantasyKeeper.Api.Models;

public record ParsedWorkbook(string SheetName, IReadOnlyList<StoredTeamKeepers> Teams);

public record ImportBlockPreview(int BlockIndex, string RawNameInSheet, string? SuggestedTeamId);

public record ImportPreview(string FileName, string SheetName, IReadOnlyList<ImportBlockPreview> Blocks);

public record BlockAssignment(int BlockIndex, string? TeamId);

public record ConfirmImportRequest(IReadOnlyList<BlockAssignment> Assignments);
