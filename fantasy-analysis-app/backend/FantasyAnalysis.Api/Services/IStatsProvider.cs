using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public interface IStatsProvider
{
    Task<IReadOnlyList<MlbPlayer>> GetAllActivePlayersAsync(int season);
    Task<IReadOnlyList<StatLine>> GetPlayerStatsAsync(IReadOnlyList<string> playerIds, int season);
}
