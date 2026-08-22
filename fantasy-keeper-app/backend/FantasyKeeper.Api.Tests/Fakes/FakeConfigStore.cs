using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Tests.Fakes;

public class FakeConfigStore : IConfigStore
{
    public List<Season> Seasons { get; set; } = new();
    public List<Team> Teams { get; set; } = new();
    public Dictionary<string, Dictionary<string, TeamMapping>> Mappings { get; set; } = new();

    public IReadOnlyList<Season> GetSeasons() => Seasons;
    public void SaveSeasons(IReadOnlyList<Season> seasons) => Seasons = seasons.ToList();
    public IReadOnlyList<Team> GetTeams() => Teams;

    public IReadOnlyDictionary<string, TeamMapping> GetTeamMappings(string seasonId) =>
        Mappings.TryGetValue(seasonId, out var m) ? m : new Dictionary<string, TeamMapping>();

    public void SaveTeamMappings(string seasonId, IReadOnlyDictionary<string, TeamMapping> mappings) =>
        Mappings[seasonId] = mappings.ToDictionary(kv => kv.Key, kv => kv.Value);
}
