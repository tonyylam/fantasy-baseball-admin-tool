using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;

namespace FantasyAnalysis.Api.Tests.Fakes;

public class FakeLeagueDataStore : ILeagueDataStore
{
    public League? Saved { get; private set; }

    public League? LoadLeague() => Saved;

    public void SaveLeague(League league) => Saved = league;
}
