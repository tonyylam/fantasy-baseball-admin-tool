using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using DriveFile = Google.Apis.Drive.v3.Data.File;
using DrivePermission = Google.Apis.Drive.v3.Data.Permission;

namespace FantasyKeeper.Api.Services.Google;

public class GoogleDriveClient : IDriveClient
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private readonly DriveService _service;

    public GoogleDriveClient(GoogleCredential credential)
    {
        _service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "FantasyKeeper"
        });
    }

    public Task<string> CopyFileAsync(string fileId, string newTitle, CancellationToken ct = default) =>
        RetryPolicy.WithOneRetryAsync(async () =>
        {
            var copyMetadata = new DriveFile { Name = newTitle };
            var request = _service.Files.Copy(copyMetadata, fileId);
            var result = await request.ExecuteAsync(ct);
            return result.Id;
        }, RetryDelay, ct);

    public Task ShareFileAsync(string fileId, string email, CancellationToken ct = default) =>
        RetryPolicy.WithOneRetryAsync(async () =>
        {
            var permission = new DrivePermission { Type = "user", Role = "writer", EmailAddress = email };
            var request = _service.Permissions.Create(permission, fileId);
            request.SendNotificationEmail = false;
            await request.ExecuteAsync(ct);
        }, RetryDelay, ct);
}
