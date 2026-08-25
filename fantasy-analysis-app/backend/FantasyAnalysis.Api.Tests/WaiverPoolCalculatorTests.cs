using System.Collections.Generic;
using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class WaiverPoolCalculatorTests
{
    [Fact]
    public void ComputeWaiverPool_ExcludesEveryRosteredPlayerAcrossAllTeams()
    {
        var allPlayers = new List<MlbPlayer>
        {
            new("660271", "Shohei Ohtani", "DH", false, 119),
            new("665742", "Juan Soto", "OF", false, 121),
            new("605483", "Gerrit Cole", "P", true, 147)
        };
        var league = new League(
            System.DateTimeOffset.UtcNow,
            new List<TeamRoster>
            {
                new("Rhino Wranglers", new List<RosteredPlayer>
                {
                    new("Shohei Ohtani", "660271", "Shohei Ohtani", "DH", false)
                }),
                new("Sea Dogs", new List<RosteredPlayer>
                {
                    new("Gerrit Cole", "605483", "Gerrit Cole", "P", true)
                })
            });

        var pool = new WaiverPoolCalculator().ComputeWaiverPool(allPlayers, league);

        var player = Assert.Single(pool);
        Assert.Equal("665742", player.Id);
    }
}
