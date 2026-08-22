using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Tests.Fakes;

public class FakeDriveClient : IDriveClient
{
    public List<(string FileId, string NewTitle)> Copies { get; } = new();
    public List<(string FileId, string Email)> Shares { get; } = new();
    public string NextCopyId { get; set; } = "copied-sheet-id";

    public Task<string> CopyFileAsync(string fileId, string newTitle, CancellationToken ct = default)
    {
        Copies.Add((fileId, newTitle));
        return Task.FromResult(NextCopyId);
    }

    public Task ShareFileAsync(string fileId, string email, CancellationToken ct = default)
    {
        Shares.Add((fileId, email));
        return Task.CompletedTask;
    }
}
