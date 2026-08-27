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
        var yourStandings = standings.Standings.Where(s => s.TeamName == yourTeamName).ToList();
        var presentCategoryKeys = yourStandings.Select(s => s.CategoryKey).ToHashSet();

        // A category can be entirely absent from your team's rows because ComputeStandings excludes
        // teams with no computable value in that category (e.g. zero innings pitched for ERA/WHIP/K9).
        // That's the worst possible state - worse than any actual worst-rank finish - so any category
        // scored elsewhere in this league's standings but missing for your team is prioritized first.
        var missingCategoryKeys = standings.Standings
            .Select(s => s.CategoryKey)
            .Distinct()
            .Where(k => !presentCategoryKeys.Contains(k))
            .ToList();

        var worstRankedPresentKeys = yourStandings
            .OrderByDescending(s => s.Rank)
            .Select(s => s.CategoryKey)
            .ToList();

        var weakCategoryKeys = missingCategoryKeys
            .Concat(worstRankedPresentKeys)
            .Take(WeakCategoryCount)
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
