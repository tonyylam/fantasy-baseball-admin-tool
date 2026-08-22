using Google.Apis.Auth.OAuth2;

namespace FantasyKeeper.Api.Services.Google;

public static class GoogleCredentialLoader
{
    public static GoogleCredential LoadFromFile(string keyFilePath, params string[] scopes)
    {
        if (!File.Exists(keyFilePath))
        {
            throw new FileNotFoundException(
                $"Google service account key file not found at '{keyFilePath}'. " +
                "See README.md 'Google Cloud setup' for how to create one.", keyFilePath);
        }

        var credential = CredentialFactory.FromFile(keyFilePath, JsonCredentialParameters.ServiceAccountCredentialType);
        return credential.CreateScoped(scopes);
    }
}
