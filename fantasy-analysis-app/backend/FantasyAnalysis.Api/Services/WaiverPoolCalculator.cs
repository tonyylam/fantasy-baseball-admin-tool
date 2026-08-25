using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class WaiverPoolCalculator
{
    public IReadOnlyList<MlbPlayer> ComputeWaiverPool(IReadOnlyList<MlbPlayer> allActivePlayers, League league)
    {
        var rosteredIds = league.Teams
            .SelectMany(t => t.Players)
            .Select(p => p.PlayerId)
            .ToHashSet();

        return allActivePlayers.Where(p => !rosteredIds.Contains(p.Id)).ToList();
    }
}
