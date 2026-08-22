namespace FantasyKeeper.Api.Services;

public interface IDriveClient
{
    Task<string> CopyFileAsync(string fileId, string newTitle, CancellationToken ct = default);
    Task ShareFileAsync(string fileId, string email, CancellationToken ct = default);
}
