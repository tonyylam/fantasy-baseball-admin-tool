namespace FantasyKeeper.Api.Services;

public interface ISheetsClient
{
    Task<IReadOnlyList<IReadOnlyList<string>>> GetRangeAsync(string spreadsheetId, string sheetTab, string range, CancellationToken ct = default);
    Task UpdateRangeAsync(string spreadsheetId, string sheetTab, string range, IReadOnlyList<IReadOnlyList<string>> values, CancellationToken ct = default);
}
