using FantasyKeeper.Api.Models;

namespace FantasyKeeper.Api.Services;

public interface IConfigStore
{
    IReadOnlyList<Team> GetTeams();
}
