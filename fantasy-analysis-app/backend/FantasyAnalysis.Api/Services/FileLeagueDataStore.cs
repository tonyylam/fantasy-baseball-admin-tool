using System.Text.Json;
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class FileLeagueDataStore : ILeagueDataStore
{
    private readonly string _dataRoot;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public FileLeagueDataStore(string dataRoot)
    {
        _dataRoot = dataRoot;
    }

    private string LeaguePath => Path.Combine(_dataRoot, "league.json");

    public League? LoadLeague()
    {
        if (!File.Exists(LeaguePath)) return null;
        return JsonSerializer.Deserialize<League>(File.ReadAllText(LeaguePath), JsonOptions);
    }

    public void SaveLeague(League league)
    {
        Directory.CreateDirectory(_dataRoot);
        var tempPath = LeaguePath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(league, JsonOptions));
            File.Move(tempPath, LeaguePath, overwrite: true);
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
