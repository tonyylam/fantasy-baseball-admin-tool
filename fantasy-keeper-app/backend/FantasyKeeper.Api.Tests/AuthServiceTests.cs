using FantasyKeeper.Api.Models;
using FantasyKeeper.Api.Services;
using FantasyKeeper.Api.Tests.Fakes;
using Xunit;

namespace FantasyKeeper.Api.Tests;

public class AuthServiceTests
{
    private static FakeConfigStore BuildStore() => new()
    {
        Teams = new List<Team> { new("b-squared", "B Squared", "1111") },
        Seasons = new List<Season> { new("2026", "2026 Season", "sheet-1", "active", DateTimeOffset.UtcNow) }
    };

    [Fact]
    public void ResolvePin_AdminPin_ReturnsAdminRole()
    {
        var service = new AuthService(BuildStore(), "9999");
        var result = service.ResolvePin("9999");

        Assert.NotNull(result);
        Assert.Equal(AuthRole.Admin, result!.Role);
    }

    [Fact]
    public void ResolvePin_TeamPin_ReturnsOwnerWithActiveSeason()
    {
        var service = new AuthService(BuildStore(), "9999");
        var result = service.ResolvePin("1111");

        Assert.NotNull(result);
        Assert.Equal(AuthRole.Owner, result!.Role);
        Assert.Equal("b-squared", result.TeamId);
        Assert.Equal("2026", result.SeasonId);
    }

    [Fact]
    public void ResolvePin_UnknownPin_ReturnsNull()
    {
        var service = new AuthService(BuildStore(), "9999");
        Assert.Null(service.ResolvePin("0000"));
    }
}
