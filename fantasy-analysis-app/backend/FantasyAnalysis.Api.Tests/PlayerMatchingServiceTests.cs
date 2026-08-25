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
}
