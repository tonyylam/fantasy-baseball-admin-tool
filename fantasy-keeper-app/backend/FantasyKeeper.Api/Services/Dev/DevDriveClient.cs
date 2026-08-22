namespace FantasyKeeper.Api.Services.Dev;

public class DevDriveClient : IDriveClient
{
    private int _copyCount;

    public Task<string> CopyFileAsync(string fileId, string newTitle, CancellationToken ct = default)
    {
        _copyCount++;
        return Task.FromResult($"dev-sheet-copy-{_copyCount}");
    }

    public Task ShareFileAsync(string fileId, string email, CancellationToken ct = default) => Task.CompletedTask;
}
