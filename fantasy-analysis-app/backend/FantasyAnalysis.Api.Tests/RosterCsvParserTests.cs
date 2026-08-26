using FantasyAnalysis.Api.Models;
using FantasyAnalysis.Api.Services;
using Xunit;

namespace FantasyAnalysis.Api.Tests;

public class RosterCsvParserTests
{
    [Fact]
    public void Parse_GroupsPlayersByTeamInFileOrder()
    {
        var csv = "Team,Player\nRhino Wranglers,Shohei Ohtani\nRhino Wranglers,Mookie Betts\nSea Dogs,Ronald Acuna Jr.\n";

        var result = new RosterCsvParser().Parse(csv);

        Assert.Equal(2, result.Teams.Count);
        Assert.Equal("Rhino Wranglers", result.Teams[0].TeamName);
        Assert.Equal(new[] { "Shohei Ohtani", "Mookie Betts" }, result.Teams[0].Players.Select(p => p.PlayerName));
        Assert.Equal("Sea Dogs", result.Teams[1].TeamName);
        Assert.Equal(new[] { "Ronald Acuna Jr." }, result.Teams[1].Players.Select(p => p.PlayerName));
    }

    [Fact]
    public void Parse_MissingHeader_ThrowsCsvParseException()
    {
        var csv = "Rhino Wranglers,Shohei Ohtani\n";

        Assert.Throws<CsvParseException>(() => new RosterCsvParser().Parse(csv));
    }

    [Fact]
    public void Parse_RowWithWrongColumnCount_ThrowsCsvParseException()
    {
        var csv = "Team,Player\nRhino Wranglers,Shohei Ohtani,ExtraColumn\n";

        Assert.Throws<CsvParseException>(() => new RosterCsvParser().Parse(csv));
    }

    [Fact]
    public void Parse_FantasyTeamHeaderWithExtraPositionColumn_CapturesPositionAndParsesCorrectly()
    {
        var csv = "Fantasy Team,Player,Position\nB Squared,K Ruiz,C\nB Squared,B Harper,1B\nBA Bombers,B Rice,C\n";

        var result = new RosterCsvParser().Parse(csv);

        Assert.Equal(2, result.Teams.Count);
        Assert.Equal("B Squared", result.Teams[0].TeamName);
        Assert.Equal(new[] { "K Ruiz", "B Harper" }, result.Teams[0].Players.Select(p => p.PlayerName));
        Assert.Equal(new[] { "C", "1B" }, result.Teams[0].Players.Select(p => p.Position));
        Assert.Equal("BA Bombers", result.Teams[1].TeamName);
        Assert.Equal(new[] { "B Rice" }, result.Teams[1].Players.Select(p => p.PlayerName));
    }

    [Fact]
    public void Parse_SnakeCaseHeaderWithSeparateProTeamColumn_CapturesFantasyTeamAndProTeamSeparately()
    {
        var csv = "fantasy_team_name,position,player_name,team\nB Squared,C,Keibert Ruiz,WAS\nB Squared,1B,Bryce Harper,PHI\n";

        var result = new RosterCsvParser().Parse(csv);

        var team = Assert.Single(result.Teams);
        Assert.Equal("B Squared", team.TeamName);
        Assert.Equal(new[] { "Keibert Ruiz", "Bryce Harper" }, team.Players.Select(p => p.PlayerName));
        Assert.Equal(new[] { "C", "1B" }, team.Players.Select(p => p.Position));
        Assert.Equal(new[] { "WAS", "PHI" }, team.Players.Select(p => p.ProTeamAbbreviation));
    }

    [Fact]
    public void Parse_RepeatedHeaderRowMidFile_SkipsItInsteadOfParsingAsData()
    {
        var csv = "fantasy_team_name,position,player_name,team\n"
            + "B Squared,C,Keibert Ruiz,WAS\n"
            + "fantasy_team_name,position,player_name,team\n"
            + "Craner Raiders,C,Hunter Goodman,COL\n";

        var result = new RosterCsvParser().Parse(csv);

        Assert.Equal(2, result.Teams.Count);
        Assert.Equal("B Squared", result.Teams[0].TeamName);
        Assert.Equal("Keibert Ruiz", Assert.Single(result.Teams[0].Players).PlayerName);
        Assert.Equal("Craner Raiders", result.Teams[1].TeamName);
        Assert.Equal("Hunter Goodman", Assert.Single(result.Teams[1].Players).PlayerName);
    }
}
