using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public interface IStatsCache
{
    IReadOnlyList<StatLine>? GetIfFresh(int season, TimeSpan maxAge);
    void Store(int season, IReadOnlyList<StatLine> statLines);
}
