namespace FantasyKeeper.Api.Services.Dev;

public class DevSheetsClient : ISheetsClient
{
    private readonly Dictionary<string, List<List<string>>> _data = new();

    public DevSheetsClient()
    {
        Seed("dev-sheet-2026", "2026 Keepers", "H8:N13", new List<List<string>>
        {
            new() { "T. Story", "#1 - 2/3", "3", "8", "8", "281", "" },
            new() { "", "", "", "", "", "", "" },
            new() { "", "", "", "", "", "", "" },
            new() { "", "", "", "", "", "", "" },
            new() { "", "", "", "", "", "", "" },
            new() { "", "", "", "", "", "", "" }
        });

        Seed("dev-sheet-2026", "2026 Keepers", "C8:F13", new List<List<string>>
        {
            new() { "T. Story", "1", "14", "2" },
            new() { "", "", "", "" },
            new() { "", "", "", "" },
            new() { "", "", "", "" },
            new() { "", "", "", "" },
            new() { "", "", "", "" }
        });
    }

    private void Seed(string spreadsheetId, string tab, string range, List<List<string>> values) =>
        _data[Key(spreadsheetId, tab, range)] = values;

    public Task<IReadOnlyList<IReadOnlyList<string>>> GetRangeAsync(string spreadsheetId, string sheetTab, string range, CancellationToken ct = default)
    {
        var values = _data.TryGetValue(Key(spreadsheetId, sheetTab, range), out var v) ? v : new List<List<string>>();
        return Task.FromResult((IReadOnlyList<IReadOnlyList<string>>)values.Select(r => (IReadOnlyList<string>)r).ToList());
    }

    public Task UpdateRangeAsync(string spreadsheetId, string sheetTab, string range, IReadOnlyList<IReadOnlyList<string>> values, CancellationToken ct = default)
    {
        _data[Key(spreadsheetId, sheetTab, range)] = values.Select(r => r.ToList()).ToList();
        return Task.CompletedTask;
    }

    private static string Key(string spreadsheetId, string tab, string range) => $"{spreadsheetId}|{tab}|{range}";
}
