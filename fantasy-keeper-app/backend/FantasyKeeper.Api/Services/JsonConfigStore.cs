using System.Text.Json;
using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public class JsonConfigStore : IConfigStore
{
    private readonly string _configRoot;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public JsonConfigStore(string configRoot)
    {
        _configRoot = configRoot;
    }

    public IReadOnlyList<Season> GetSeasons() =>
        ReadJson<List<Season>>(Path.Combine(_configRoot, "seasons.json")) ?? new List<Season>();

    public void SaveSeasons(IReadOnlyList<Season> seasons) =>
        WriteJson(Path.Combine(_configRoot, "seasons.json"), seasons);

    public IReadOnlyList<Team> GetTeams() =>
        ReadJson<List<Team>>(Path.Combine(_configRoot, "teams.json")) ?? new List<Team>();

    public IReadOnlyDictionary<string, TeamMapping> GetTeamMappings(string seasonId) =>
        ReadJson<Dictionary<string, TeamMapping>>(Path.Combine(_configRoot, "team-mappings", $"{seasonId}.json"))
        ?? new Dictionary<string, TeamMapping>();

    public void SaveTeamMappings(string seasonId, IReadOnlyDictionary<string, TeamMapping> mappings)
    {
        var path = Path.Combine(_configRoot, "team-mappings", $"{seasonId}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteJson(path, mappings);
    }

    private static T? ReadJson<T>(string path)
    {
        if (!File.Exists(path)) return default;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    }
}
