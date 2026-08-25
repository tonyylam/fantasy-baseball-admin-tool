using System.Globalization;
using System.Text;
using FantasyAnalysis.Api.Models;

namespace FantasyAnalysis.Api.Services;

public class PlayerMatchingService : IPlayerMatchingService
{
    private const double MatchThreshold = 0.7;
    private const int MaxCandidates = 5;

    public IReadOnlyList<PlayerMatch> MatchPlayers(IReadOnlyList<string> csvNames, IReadOnlyList<MlbPlayer> candidatePool)
    {
        var normalizedPool = candidatePool
            .Select(p => (Player: p, Normalized: Normalize(p.FullName)))
            .ToList();

        var matches = new List<PlayerMatch>();
        foreach (var csvName in csvNames)
        {
            var normalizedCsvName = Normalize(csvName);

            var scored = normalizedPool
                .Select(p => new PlayerMatchCandidate(p.Player.Id, p.Player.FullName, p.Player.Position, p.Player.IsPitcher, Similarity(normalizedCsvName, p.Normalized)))
                .OrderByDescending(c => c.Score)
                .Take(MaxCandidates)
                .ToList();

            var bestGuess = scored.FirstOrDefault();
            matches.Add(new PlayerMatch(
                csvName,
                bestGuess is not null && bestGuess.Score >= MatchThreshold ? bestGuess : null,
                scored));
        }

        return matches;
    }

    private static string Normalize(string name)
    {
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var stripped = new string(decomposed
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());

        var lower = stripped.ToLowerInvariant().Replace(".", "");
        var words = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w is not ("jr" or "sr" or "ii" or "iii" or "iv"));
        return string.Join(" ", words);
    }

    private static double Similarity(string a, string b)
    {
        if (a == b) return 1.0;
        var maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0) return 1.0;
        return 1.0 - (double)LevenshteinDistance(a, b) / maxLen;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }

        return d[a.Length, b.Length];
    }
}
