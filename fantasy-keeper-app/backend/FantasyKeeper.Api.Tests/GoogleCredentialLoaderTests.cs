using FantasyKeeper.Api.Services.Google;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class GoogleCredentialLoaderTests
{
    [Fact]
    public void LoadFromFile_MissingFile_ThrowsFileNotFoundException()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "key.json");

        var ex = Assert.Throws<FileNotFoundException>(() =>
            GoogleCredentialLoader.LoadFromFile(missingPath, "https://www.googleapis.com/auth/spreadsheets"));

        Assert.Contains("service account key file not found", ex.Message);
    }
}
