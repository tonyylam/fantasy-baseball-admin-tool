using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public interface IConfigStore
{
    IReadOnlyList<Season> GetSeasons();
    void SaveSeasons(IReadOnlyList<Season> seasons);
    IReadOnlyList<Team> GetTeams();
    IReadOnlyDictionary<string, TeamMapping> GetTeamMappings(string seasonId);
    void SaveTeamMappings(string seasonId, IReadOnlyDictionary<string, TeamMapping> mappings);
}
