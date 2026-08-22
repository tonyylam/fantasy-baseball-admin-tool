using FantasyKeeper.Api.Services.Dev;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class DevClientsTests
{
    [Fact]
    public async Task DevSheetsClient_GetRange_ReturnsSeededData()
    {
        var client = new DevSheetsClient();

        var values = await client.GetRangeAsync("dev-sheet-2026", "2026 Keepers", "C8:F13");

        Assert.Equal(6, values.Count);
        Assert.Equal("T. Story", values[0][0]);
    }

    [Fact]
    public async Task DevSheetsClient_UpdateThenGet_RoundTrips()
    {
        var client = new DevSheetsClient();
        var newValues = new List<IReadOnlyList<string>> { new List<string> { "New Guy", "1", "10", "2" } };

        await client.UpdateRangeAsync("dev-sheet-2026", "2026 Keepers", "C8:F8", newValues);
        var result = await client.GetRangeAsync("dev-sheet-2026", "2026 Keepers", "C8:F8");

        Assert.Equal("New Guy", result[0][0]);
    }

    [Fact]
    public async Task DevDriveClient_CopyFile_ReturnsDistinctIds()
    {
        var client = new DevDriveClient();

        var first = await client.CopyFileAsync("dev-sheet-2026", "2027 Season");
        var second = await client.CopyFileAsync("dev-sheet-2026", "2028 Season");

        Assert.NotEqual(first, second);
    }
}
