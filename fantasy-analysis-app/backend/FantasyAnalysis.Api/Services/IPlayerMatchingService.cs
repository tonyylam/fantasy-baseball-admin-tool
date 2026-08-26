using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public interface IPlayerMatchingService
{
    IReadOnlyList<PlayerMatch> MatchPlayers(IReadOnlyList<ParsedPlayer> players, IReadOnlyList<MlbPlayer> candidatePool);
}
