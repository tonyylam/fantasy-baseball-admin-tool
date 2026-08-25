using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public interface IPlayerMatchingService
{
    IReadOnlyList<PlayerMatch> MatchPlayers(IReadOnlyList<string> csvNames, IReadOnlyList<MlbPlayer> candidatePool);
}
