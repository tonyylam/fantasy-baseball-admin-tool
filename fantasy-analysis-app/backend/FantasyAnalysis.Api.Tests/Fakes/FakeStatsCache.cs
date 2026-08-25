using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;

namespace FantasyAnalysis.Api.Tests.Fakes;

public class FakeStatsCache : IStatsCache
{
    private readonly Dictionary<int, IReadOnlyList<StatLine>> _stored = new();

    public IReadOnlyList<StatLine>? GetIfFresh(int season, TimeSpan maxAge) =>
        _stored.TryGetValue(season, out var lines) ? lines : null;

    public void Store(int season, IReadOnlyList<StatLine> statLines) => _stored[season] = statLines;
}
