using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Tests.Fakes;

public class FakeSheetsClient : ISheetsClient
{
    private readonly Dictionary<string, IReadOnlyList<IReadOnlyList<string>>> _data = new();
    public List<(string SpreadsheetId, string Tab, string Range, IReadOnlyList<IReadOnlyList<string>> Values)> Updates { get; } = new();

    public void Seed(string spreadsheetId, string tab, string range, IReadOnlyList<IReadOnlyList<string>> values) =>
        _data[Key(spreadsheetId, tab, range)] = values;

    public Task<IReadOnlyList<IReadOnlyList<string>>> GetRangeAsync(string spreadsheetId, string sheetTab, string range, CancellationToken ct = default)
    {
        return Task.FromResult(_data.TryGetValue(Key(spreadsheetId, sheetTab, range), out var values)
            ? values
            : (IReadOnlyList<IReadOnlyList<string>>)new List<IReadOnlyList<string>>());
    }

    public Task UpdateRangeAsync(string spreadsheetId, string sheetTab, string range, IReadOnlyList<IReadOnlyList<string>> values, CancellationToken ct = default)
    {
        Updates.Add((spreadsheetId, sheetTab, range, values));
        _data[Key(spreadsheetId, sheetTab, range)] = values;
        return Task.CompletedTask;
    }

    private static string Key(string spreadsheetId, string tab, string range) => $"{spreadsheetId}|{tab}|{range}";
}
