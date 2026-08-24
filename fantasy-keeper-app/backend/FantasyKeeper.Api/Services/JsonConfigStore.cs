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

    public IReadOnlyList<Team> GetTeams() =>
        ReadJson<List<Team>>(Path.Combine(_configRoot, "teams.json")) ?? new List<Team>();

    private static T? ReadJson<T>(string path)
    {
        if (!File.Exists(path)) return default;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}
