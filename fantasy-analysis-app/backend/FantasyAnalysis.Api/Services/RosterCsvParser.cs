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

        if (lines.Count == 0)
        {
            throw new CsvParseException("CSV is empty.");
        }

        var headerColumns = lines[0].Split(',').Select(c => c.Trim()).ToList();
        var teamIndex = FindColumnIndex(headerColumns, "team", "fantasy team");
        var playerIndex = FindColumnIndex(headerColumns, "player");

        if (teamIndex is null || playerIndex is null)
        {
            throw new CsvParseException("Expected a header row with \"Team\" (or \"Fantasy Team\") and \"Player\" columns.");
        }

        var teams = new List<ParsedTeamRoster>();
        var playersByTeam = new Dictionary<string, List<string>>();
        var teamOrder = new List<string>();

        for (var i = 1; i < lines.Count; i++)
        {
            var columns = lines[i].Split(',');
            if (columns.Length != headerColumns.Count)
            {
                throw new CsvParseException($"Line {i + 1}: expected {headerColumns.Count} columns, found {columns.Length}.");
            }

            var teamName = columns[teamIndex.Value].Trim();
            var playerName = columns[playerIndex.Value].Trim();

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

    private static int? FindColumnIndex(List<string> headerColumns, params string[] acceptedNames)
    {
        for (var i = 0; i < headerColumns.Count; i++)
        {
            if (acceptedNames.Any(name => string.Equals(headerColumns[i], name, StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }
        return null;
    }
}
