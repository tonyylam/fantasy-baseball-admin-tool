using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace FantasyKeeper.Api.Services.Google;

public class GoogleSheetsClient : ISheetsClient
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private readonly SheetsService _service;

    public GoogleSheetsClient(GoogleCredential credential)
    {
        _service = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "FantasyKeeper"
        });
    }

    public Task<IReadOnlyList<IReadOnlyList<string>>> GetRangeAsync(string spreadsheetId, string sheetTab, string range, CancellationToken ct = default) =>
        RetryPolicy.WithOneRetryAsync(async () =>
        {
            var request = _service.Spreadsheets.Values.Get(spreadsheetId, $"'{sheetTab}'!{range}");
            request.ValueRenderOption = SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.FORMATTEDVALUE;
            var response = await request.ExecuteAsync(ct);

            return (IReadOnlyList<IReadOnlyList<string>>)(response.Values ?? new List<IList<object>>())
                .Select(row => (IReadOnlyList<string>)row.Select(cell => cell?.ToString() ?? "").ToList())
                .ToList();
        }, RetryDelay, ct);

    public Task UpdateRangeAsync(string spreadsheetId, string sheetTab, string range, IReadOnlyList<IReadOnlyList<string>> values, CancellationToken ct = default) =>
        RetryPolicy.WithOneRetryAsync(async () =>
        {
            var body = new ValueRange
            {
                Values = values.Select(row => (IList<object>)row.Select(v => (object)v).ToList()).ToList()
            };

            var request = _service.Spreadsheets.Values.Update(body, spreadsheetId, $"'{sheetTab}'!{range}");
            request.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
            await request.ExecuteAsync(ct);
        }, RetryDelay, ct);
}
