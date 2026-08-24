using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;

namespace FantasyKeeper.Api.Tests.Fakes;

public class FakeConfigStore : IConfigStore
{
    public List<Team> Teams { get; set; } = new();

    public IReadOnlyList<Team> GetTeams() => Teams;
}
