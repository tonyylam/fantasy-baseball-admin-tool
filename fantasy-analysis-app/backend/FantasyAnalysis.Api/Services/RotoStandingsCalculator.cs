using System.Linq;
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class RotoStandingsCalculator
{
    public RotoStandings ComputeStandings(
        League league,
        IReadOnlyList<string> hittingCategoryKeys,
        IReadOnlyList<string> pitchingCategoryKeys,
        IReadOnlyDictionary<string, IReadOnlyList<StatLine>> statsByPlayerId)
    {
        var activeCategoryKeys = hittingCategoryKeys.Concat(pitchingCategoryKeys).ToList();
        var standings = new List<TeamCategoryStanding>();

        foreach (var categoryKey in activeCategoryKeys)
        {
            var definition = RotoCategoryReference.Categories[categoryKey];

            var teamValues = league.Teams
                .Select(t => (Team: t, Value: RotoStatMath.ComputeCategoryValue(
                    t.Players.SelectMany(p => statsByPlayerId.TryGetValue(p.PlayerId, out var lines) ? lines : Array.Empty<StatLine>()),
                    definition)))
                .Where(x => x.Value is not null)
                .ToList();

            var bestFirst = definition.Direction == StatDirection.HigherIsBetter
                ? teamValues.OrderByDescending(x => x.Value!.Value).ToList()
                : teamValues.OrderBy(x => x.Value!.Value).ToList();

            var rankByTeamName = AssignRanksWithTiesSplit(bestFirst.Select(x => (x.Team.TeamName, x.Value!.Value)).ToList());
            var teamCount = bestFirst.Count;

            foreach (var (team, value) in bestFirst.Select(x => (x.Team, x.Value!.Value)))
            {
                var rank = rankByTeamName[team.TeamName];
                standings.Add(new TeamCategoryStanding(team.TeamName, categoryKey, value, rank, teamCount - rank + 1));
            }
        }

        return new RotoStandings(standings);
    }

    private static Dictionary<string, decimal> AssignRanksWithTiesSplit(List<(string TeamName, decimal Value)> bestFirstOrder)
    {
        var result = new Dictionary<string, decimal>();
        var i = 0;
        while (i < bestFirstOrder.Count)
        {
            var j = i;
            while (j + 1 < bestFirstOrder.Count && bestFirstOrder[j + 1].Value == bestFirstOrder[i].Value)
            {
                j++;
            }
            // Ranks i+1..j+1 (1-based) are tied; each tied team gets the average of those ranks.
            var averageRank = Enumerable.Range(i + 1, j - i + 1).Average(r => (decimal)r);
            for (var k = i; k <= j; k++)
            {
                result[bestFirstOrder[k].TeamName] = averageRank;
            }
            i = j + 1;
        }
        return result;
    }
}
