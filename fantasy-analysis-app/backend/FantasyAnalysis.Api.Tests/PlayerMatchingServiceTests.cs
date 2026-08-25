using System.Collections.Generic;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class PlayerMatchingServiceTests
{
    private static readonly List<MlbPlayer> Pool = new()
    {
        new MlbPlayer("660271", "Shohei Ohtani", "DH", false, 119),
        new MlbPlayer("665489", "Ronald Acuña Jr.", "OF", false, 144),
        new MlbPlayer("665742", "Juan Soto", "OF", false, 121)
    };

    [Fact]
    public void MatchPlayers_ExactNameMatch_ReturnsFullConfidenceBestGuess()
    {
        var service = new PlayerMatchingService();

        var matches = service.MatchPlayers(new[] { "Shohei Ohtani" }, Pool);

        var match = Assert.Single(matches);
        Assert.NotNull(match.BestGuess);
        Assert.Equal("660271", match.BestGuess!.PlayerId);
        Assert.Equal(1.0, match.BestGuess.Score, 3);
    }

    [Fact]
    public void MatchPlayers_DiacriticAndPunctuationDifference_StillMatches()
    {
        var service = new PlayerMatchingService();

        var matches = service.MatchPlayers(new[] { "Ronald Acuna Jr" }, Pool);

        var match = Assert.Single(matches);
        Assert.NotNull(match.BestGuess);
        Assert.Equal("665489", match.BestGuess!.PlayerId);
    }

    [Fact]
    public void MatchPlayers_NoCloseCandidate_ReturnsNullBestGuess()
    {
        var service = new PlayerMatchingService();

        var matches = service.MatchPlayers(new[] { "Zzyzx Nobody" }, Pool);

        var match = Assert.Single(matches);
        Assert.Null(match.BestGuess);
    }

    [Fact]
    public void MatchPlayers_FuzzyMatchWithTypo_CalculatesLevenshteinScore()
    {
        var service = new PlayerMatchingService();

        // "Shohei Otani" has a typo (missing 'h'), should still match "Shohei Ohtani"
        // After normalization: "shohei otani" vs "shohei ohtani" (1 character difference)
        // Score = 1 - (1 / max_length) ≈ 0.917, which is > 0.7 threshold
        var matches = service.MatchPlayers(new[] { "Shohei Otani" }, Pool);

        var match = Assert.Single(matches);
        Assert.NotNull(match.BestGuess);
        Assert.Equal("660271", match.BestGuess!.PlayerId);
        // Verify it's a fractional score (not 1.0, proving Levenshtein calc ran)
        Assert.True(match.BestGuess.Score > 0.7 && match.BestGuess.Score < 1.0,
            $"Expected score between 0.7 and 1.0, got {match.BestGuess.Score}");
    }

    [Fact]
    public void MatchPlayers_LargeCandidatePool_CapsCandidatesAt5()
    {
        var service = new PlayerMatchingService();

        // Build a pool with many similar names to the CSV name
        var largePool = new List<MlbPlayer>
        {
            new MlbPlayer("1", "John Smith", "OF", false, 1),
            new MlbPlayer("2", "Jon Smith", "OF", false, 2),
            new MlbPlayer("3", "John Smyth", "OF", false, 3),
            new MlbPlayer("4", "Jon Smyth", "OF", false, 4),
            new MlbPlayer("5", "John Smithe", "OF", false, 5),
            new MlbPlayer("6", "Jon Smithe", "OF", false, 6),
            new MlbPlayer("7", "Johnny Smith", "OF", false, 7),
            new MlbPlayer("8", "Shohei Ohtani", "DH", false, 8)
        };

        var matches = service.MatchPlayers(new[] { "John Smith" }, largePool);

        var match = Assert.Single(matches);
        // Should have exactly 5 candidates (the cap), not all 7 similar matches
        Assert.Equal(5, match.Candidates.Count);
        // Best match should still be the exact player (ID "1")
        Assert.NotNull(match.BestGuess);
        Assert.Equal("1", match.BestGuess!.PlayerId);
    }
}
