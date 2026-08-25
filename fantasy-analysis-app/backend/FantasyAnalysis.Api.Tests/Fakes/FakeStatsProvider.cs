using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;

namespace FantasyAnalysis.Api.Tests.Fakes;

public class FakeStatsProvider : IStatsProvider
{
    private readonly IReadOnlyList<MlbPlayer> _players;
    private readonly IReadOnlyList<StatLine> _statLines;

    public FakeStatsProvider(IReadOnlyList<MlbPlayer> players, IReadOnlyList<StatLine>? statLines = null)
    {
        _players = players;
        _statLines = statLines ?? new List<StatLine>();
    }

    public Task<IReadOnlyList<MlbPlayer>> GetAllActivePlayersAsync(int season) => Task.FromResult(_players);

    public Task<IReadOnlyList<StatLine>> GetPlayerStatsAsync(IReadOnlyList<string> playerIds, int season) =>
        Task.FromResult<IReadOnlyList<StatLine>>(_statLines.Where(s => playerIds.Contains(s.PlayerId)).ToList());
}
