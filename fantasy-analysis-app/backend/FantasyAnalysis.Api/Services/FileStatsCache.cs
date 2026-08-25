using System.Text.Json;
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class FileStatsCache : IStatsCache
{
    private readonly string _dataRoot;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public FileStatsCache(string dataRoot)
    {
        _dataRoot = dataRoot;
    }

    private string PathFor(int season) => Path.Combine(_dataRoot, $"stats-cache-{season}.json");

    public IReadOnlyList<StatLine>? GetIfFresh(int season, TimeSpan maxAge)
    {
        var path = PathFor(season);
        if (!File.Exists(path)) return null;

        var entry = JsonSerializer.Deserialize<StatsCacheEntry>(File.ReadAllText(path), JsonOptions);
        if (entry is null) return null;
        if (DateTimeOffset.UtcNow - entry.FetchedAtUtc > maxAge) return null;

        return entry.StatLines;
    }

    public void Store(int season, IReadOnlyList<StatLine> statLines)
    {
        Directory.CreateDirectory(_dataRoot);
        var entry = new StatsCacheEntry(DateTimeOffset.UtcNow, statLines);
        var path = PathFor(season);
        var tempPath = path + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(entry, JsonOptions));
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            }
            throw;
        }
    }
}
