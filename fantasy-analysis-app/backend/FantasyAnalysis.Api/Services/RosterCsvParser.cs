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

        var headerLine = lines[0];
        var headerColumns = headerLine.Split(',').Select(c => c.Trim()).ToList();
        var normalizedHeaders = headerColumns.Select(NormalizeHeaderName).ToList();

        var teamIndex = FindColumnIndex(normalizedHeaders, h => HasWord(h, "fantasy") && HasWord(h, "team"))
            ?? FindColumnIndex(normalizedHeaders, h => HasWord(h, "team"));
        var playerIndex = FindColumnIndex(normalizedHeaders, h => HasWord(h, "player"));

        if (teamIndex is null || playerIndex is null)
        {
            throw new CsvParseException("Expected a header row with \"Team\" (or \"Fantasy Team\") and \"Player\" columns.");
        }

        var positionIndex = FindColumnIndex(normalizedHeaders, h => HasWord(h, "position"));
        var proTeamIndex = FindColumnIndex(normalizedHeaders, (h, i) => i != teamIndex && HasWord(h, "team"));

        var teams = new List<ParsedTeamRoster>();
        var playersByTeam = new Dictionary<string, List<ParsedPlayer>>();
        var teamOrder = new List<string>();

        for (var i = 1; i < lines.Count; i++)
        {
            // Some exports concatenate multiple sheets/tabs and repeat the header row
            // partway through the file - skip an exact repeat rather than parsing it as
            // a bogus data row.
            if (string.Equals(lines[i], headerLine, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var columns = lines[i].Split(',');
            if (columns.Length != headerColumns.Count)
            {
                throw new CsvParseException($"Line {i + 1}: expected {headerColumns.Count} columns, found {columns.Length}.");
            }

            var teamName = columns[teamIndex.Value].Trim();
            var playerName = columns[playerIndex.Value].Trim();
            var position = positionIndex is not null ? columns[positionIndex.Value].Trim() : null;
            var proTeam = proTeamIndex is not null ? columns[proTeamIndex.Value].Trim() : null;

            if (!playersByTeam.TryGetValue(teamName, out var players))
            {
                players = new List<ParsedPlayer>();
                playersByTeam[teamName] = players;
                teamOrder.Add(teamName);
            }
            players.Add(new ParsedPlayer(
                playerName,
                string.IsNullOrEmpty(position) ? null : position,
                string.IsNullOrEmpty(proTeam) ? null : proTeam));
        }

        foreach (var teamName in teamOrder)
        {
            teams.Add(new ParsedTeamRoster(teamName, playersByTeam[teamName]));
        }

        return new ParsedLeague(teams);
    }

    private static string NormalizeHeaderName(string header)
    {
        var lower = header.ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');
        var words = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words);
    }

    private static bool HasWord(string normalizedHeader, string word) =>
        normalizedHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(word);

    private static int? FindColumnIndex(List<string> normalizedHeaders, Func<string, bool> predicate)
    {
        for (var i = 0; i < normalizedHeaders.Count; i++)
        {
            if (predicate(normalizedHeaders[i])) return i;
        }
        return null;
    }

    private static int? FindColumnIndex(List<string> normalizedHeaders, Func<string, int, bool> predicate)
    {
        for (var i = 0; i < normalizedHeaders.Count; i++)
        {
            if (predicate(normalizedHeaders[i], i)) return i;
        }
        return null;
    }
}
