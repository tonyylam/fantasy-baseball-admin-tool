using System.Text.Json;
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class FileScoringSettingsStore : IScoringSettingsStore
{
    private readonly string _dataRoot;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public FileScoringSettingsStore(string dataRoot)
    {
        _dataRoot = dataRoot;
    }

    private string SettingsPath => Path.Combine(_dataRoot, "scoring-settings.json");

    public ScoringSettings? Load()
    {
        if (!File.Exists(SettingsPath)) return null;
        return JsonSerializer.Deserialize<ScoringSettings>(File.ReadAllText(SettingsPath), JsonOptions);
    }

    public void Save(ScoringSettings settings)
    {
        Directory.CreateDirectory(_dataRoot);
        var tempPath = SettingsPath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tempPath, SettingsPath, overwrite: true);
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
