using System.Linq;
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class WeakCategoryWaiverShortlist
{
    private const int WeakCategoryCount = 3;
    private const int TopCandidatesPerCategory = 5;
    private const decimal MinimumPlateAppearances = 50m;
    private const decimal MinimumInningsPitched = 20m;

    public IReadOnlyDictionary<string, IReadOnlyList<MlbPlayer>> ShortlistForWeakCategories(
        RotoStandings standings,
        string yourTeamName,
        IReadOnlyList<MlbPlayer> waiverPool,
        IReadOnlyDictionary<string, IReadOnlyList<StatLine>> statsByPlayerId)
    {
        var weakCategoryKeys = standings.Standings
            .Where(s => s.TeamName == yourTeamName)
            .OrderByDescending(s => s.Rank)
            .Take(WeakCategoryCount)
            .Select(s => s.CategoryKey)
            .ToList();

        var result = new Dictionary<string, IReadOnlyList<MlbPlayer>>();
        foreach (var categoryKey in weakCategoryKeys)
        {
            var definition = RotoCategoryReference.Categories[categoryKey];

            var scored = waiverPool
                .Select(p => (Player: p, Lines: statsByPlayerId.TryGetValue(p.Id, out var lines) ? lines : Array.Empty<StatLine>()))
                .Where(x => !definition.IsRateStat || MeetsSampleSizeFloor(x.Lines, definition))
                .Select(x => (x.Player, Value: RotoStatMath.ComputeCategoryValue(x.Lines, definition)))
                .Where(x => x.Value is not null)
                .ToList();

            var ordered = definition.Direction == StatDirection.HigherIsBetter
                ? scored.OrderByDescending(x => x.Value!.Value)
                : scored.OrderBy(x => x.Value!.Value);

            result[categoryKey] = ordered.Take(TopCandidatesPerCategory).Select(x => x.Player).ToList();
        }

        return result;
    }

    private static bool MeetsSampleSizeFloor(IReadOnlyList<StatLine> lines, RotoCategoryDefinition definition)
    {
        var relevantLine = lines.FirstOrDefault(l => l.Group == definition.Group);
        if (relevantLine is null) return false;

        if (definition.Group == "hitting")
        {
            return relevantLine.Stats.TryGetValue("plateAppearances", out var pa) && pa >= MinimumPlateAppearances;
        }

        return relevantLine.Stats.TryGetValue("inningsPitched", out var ip)
            && RotoStatMath.ConvertToTrueInnings(ip) >= MinimumInningsPitched;
    }
}
