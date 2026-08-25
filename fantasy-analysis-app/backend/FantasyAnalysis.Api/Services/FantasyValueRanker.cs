using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class FantasyValueRanker
{
    public decimal ComputePlayerValue(IReadOnlyList<StatLine> playerStatLines, ScoringSettings settings)
    {
        decimal total = 0;
        foreach (var line in playerStatLines)
        {
            var categories = line.Group == "pitching" ? settings.PitchingCategories : settings.HittingCategories;
            foreach (var category in categories)
            {
                if (line.Stats.TryGetValue(category.StatKey, out var value))
                {
                    total += value * category.PointsPerUnit;
                }
            }
        }
        return total;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<MlbPlayer>> TopCandidatesByPosition(
        IReadOnlyList<MlbPlayer> candidates,
        IReadOnlyDictionary<string, IReadOnlyList<StatLine>> statsByPlayerId,
        ScoringSettings settings,
        int topNPerPosition)
    {
        return candidates
            .GroupBy(p => p.Position)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<MlbPlayer>)g
                    .Select(p => (Player: p, Value: ComputePlayerValue(
                        statsByPlayerId.TryGetValue(p.Id, out var lines) ? lines : Array.Empty<StatLine>(),
                        settings)))
                    .OrderByDescending(x => x.Value)
                    .Take(topNPerPosition)
                    .Select(x => x.Player)
                    .ToList());
    }
}
