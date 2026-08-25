using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public interface ILeagueDataStore
{
    League? LoadLeague();
    void SaveLeague(League league);
}
