using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class RosterCsvParser
{
    public ParsedLeague Parse(string csvContent)
    {
        var lines = csvContent
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0)
            .ToList();

        if (lines.Count == 0 || !string.Equals(lines[0], "Team,Player", StringComparison.OrdinalIgnoreCase))
        {
            throw new CsvParseException("Expected a header row \"Team,Player\".");
        }

        var teams = new List<ParsedTeamRoster>();
        var playersByTeam = new Dictionary<string, List<string>>();
        var teamOrder = new List<string>();

        for (var i = 1; i < lines.Count; i++)
        {
            var columns = lines[i].Split(',');
            if (columns.Length != 2)
            {
                throw new CsvParseException($"Line {i + 1}: expected 2 columns (Team,Player), found {columns.Length}.");
            }

            var teamName = columns[0].Trim();
            var playerName = columns[1].Trim();

            if (!playersByTeam.TryGetValue(teamName, out var players))
            {
                players = new List<string>();
                playersByTeam[teamName] = players;
                teamOrder.Add(teamName);
            }
            players.Add(playerName);
        }

        foreach (var teamName in teamOrder)
        {
            teams.Add(new ParsedTeamRoster(teamName, playersByTeam[teamName]));
        }

        return new ParsedLeague(teams);
    }
}
